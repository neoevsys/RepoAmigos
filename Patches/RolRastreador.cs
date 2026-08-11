using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// ROL: RASTREADOR.
    ///
    /// Cada X minutos le canta automaticamente donde esta el monstruo mas cercano:
    /// nombre, distancia y hacia donde queda respecto a como esta mirando.
    ///
    /// No hace falta pulsar nada. El aviso llega solo, y esa es la gracia: te
    /// enteras de que tienes algo a 8 metros a tu espalda justo cuando ya no
    /// puedes hacer gran cosa.
    ///
    /// ---------------------------------------------------------------------
    /// AUTORIDAD
    /// ---------------------------------------------------------------------
    /// SemiFunc.EnemyGetNearest(posicion, distanciaMaxima, raycast) solo LEE la
    /// lista de enemigos, no la toca, asi que corre igual de bien en un cliente
    /// que en el master. Nada de red: el aviso es informacion privada del
    /// rastreador y no debe salir de su maquina.
    ///
    /// La direccion se da en horas de reloj respecto a la camara (a las 12 = al
    /// frente, a las 6 = detras) porque en R.E.P.O. los pasillos giran mucho y un
    /// "norte" absoluto no le sirve a nadie.
    /// </summary>
    internal static class RolRastreador
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>  Enabled;
        internal static ConfigEntry<float> MinutosEntreAvisos;
        internal static ConfigEntry<float> AlcanceMaximo;
        internal static ConfigEntry<bool>  DecirNombre;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Rastreador", "Activado", true,
                "Mete el rol de rastreador en el sorteo.");

            MinutosEntreAvisos = config.Bind(
                "Rastreador", "MinutosEntreAvisos", 2f,
                new ConfigDescription("Cada cuanto se marca al monstruo mas cercano.",
                    new AcceptableValueRange<float>(0.1f, 30f)));

            AlcanceMaximo = config.Bind(
                "Rastreador", "AlcanceMaximo", 60f,
                new ConfigDescription(
                    "Distancia maxima de deteccion, en metros. Si no hay ningun monstruo " +
                    "dentro de este radio, el aviso dice que la zona esta despejada.",
                    new AcceptableValueRange<float>(5f, 300f)));

            DecirNombre = config.Bind(
                "Rastreador", "DecirNombre", true,
                "Incluye el nombre del monstruo en el aviso. Si lo apagas solo se dice " +
                "la distancia y la direccion, que da mas mal rollo.");

            Roles.AlRepartir += () => { if (Roles.SoyEl(Rol.Rastreador)) Reiniciar(); };
        }

        // =====================================================================
        // Estado
        // =====================================================================

        private static float _siguienteAviso;

        internal static void Reiniciar()
        {
            // El primer aviso no es inmediato: se espera un intervalo completo.
            _siguienteAviso = MinutosEntreAvisos.Value * 60f;
        }

        /// <summary>Como se usa el rol. Ver el comentario en RolMedico.TextoDeUso.</summary>
        internal static string TextoDeUso()
        {
            return $"Cada {MinutosEntreAvisos.Value:0} min sabras donde esta el monstruo mas cercano";
        }

        internal static void AvisarUso()
        {
            SemiFunc.UIFocusText(TextoDeUso(), Roles.ColorRastreador, Color.white, 5f);
        }

        // =====================================================================
        // Tic
        // =====================================================================

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if (!Roles.SoyEl(Rol.Rastreador)) return;
            if (!Roles.EnPartida()) return;

            _siguienteAviso -= Time.deltaTime;
            if (_siguienteAviso > 0f) return;

            _siguienteAviso = MinutosEntreAvisos.Value * 60f;
            Rastrear();
        }

        // =====================================================================
        // El barrido
        // =====================================================================

        private static readonly FieldInfo _campoEnemyParent =
            AccessTools.Field(typeof(Enemy), "EnemyParent");

        private static void Rastrear()
        {
            PlayerAvatar yo = PlayerAvatar.instance;
            if (yo == null) return;

            Vector3 origen = yo.playerTransform != null
                ? yo.playerTransform.position
                : yo.transform.position;

            // raycast en false: queremos saber que hay al otro lado de la pared,
            // que para eso es un rastreador.
            Enemy enemigo = SemiFunc.EnemyGetNearest(origen, AlcanceMaximo.Value, false);

            if (enemigo == null)
            {
                SemiFunc.UIFocusText("RASTREO: zona despejada",
                    new Color(0.5f, 1f, 0.6f), Color.white, 4f);
                Plugin.Debug("Rastreador: sin enemigos en rango.");
                return;
            }

            Vector3 posicion  = enemigo.transform.position;
            float   distancia = Vector3.Distance(origen, posicion);

            string nombre = DecirNombre.Value ? NombreDe(enemigo) : "Algo";
            string reloj  = DireccionEnHoras(origen, posicion);

            // Color mas rojo cuanto mas cerca: se lee de un vistazo sin procesar
            // el numero.
            float  cerca = Mathf.Clamp01(1f - distancia / AlcanceMaximo.Value);
            Color  color = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.25f, 0.25f), cerca);

            SemiFunc.UIFocusText($"RASTREO: {nombre}  a {distancia:0} m  {reloj}",
                color, Color.white, 5f);

            Plugin.Debug($"Rastreador: {nombre} a {distancia:0.0} m, {reloj}.");
        }

        private static string NombreDe(Enemy enemigo)
        {
            if (_campoEnemyParent == null) return "Monstruo";

            EnemyParent padre = _campoEnemyParent.GetValue(enemigo) as EnemyParent;
            if (padre == null || string.IsNullOrEmpty(padre.enemyName)) return "Monstruo";

            return padre.enemyName;
        }

        /// <summary>
        /// Direccion en horas de reloj respecto a hacia donde mira la camara.
        ///
        /// Se aplana la Y antes de medir: en un pasillo estrecho el angulo
        /// vertical mete un ruido enorme y "a las 3" dejaria de significar nada.
        /// </summary>
        private static string DireccionEnHoras(Vector3 origen, Vector3 destino)
        {
            Camera camara = SemiFunc.MainCamera();
            if (camara == null) return "";

            Vector3 haciaElEnemigo = destino - origen;
            Vector3 miraHacia      = camara.transform.forward;
            haciaElEnemigo.y = 0f;
            miraHacia.y      = 0f;

            if (haciaElEnemigo.sqrMagnitude < 0.001f || miraHacia.sqrMagnitude < 0.001f)
                return "encima de ti";

            // SignedAngle da -180..180 con el eje Y como referencia; pasarlo a
            // 0..360 en sentido horario es lo que necesita la esfera del reloj.
            float grados = Vector3.SignedAngle(miraHacia, haciaElEnemigo, Vector3.up);
            if (grados < 0f) grados += 360f;

            int hora = Mathf.RoundToInt(grados / 30f);
            if (hora == 0) hora = 12;

            switch (hora)
            {
                case 12: return "AL FRENTE";
                case 6:  return "A TU ESPALDA";
                case 3:  return "a tu derecha";
                case 9:  return "a tu izquierda";
                default: return $"a las {hora}";
            }
        }
    }
}
