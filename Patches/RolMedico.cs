using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// ROL: MEDICO.
    ///
    /// Cura al companero mas cercano que tenga delante. Curar no le cuesta vida
    /// (en vanilla tampoco cuesta nada, asi que no hay que quitar nada), pero
    /// lleva cargas limitadas que se recargan solas cada X minutos.
    ///
    /// ---------------------------------------------------------------------
    /// AUTORIDAD DE RED
    /// ---------------------------------------------------------------------
    /// PlayerHealth tiene tres metodos y solo uno sirve desde fuera:
    ///
    ///   Heal(int, bool)       -> arranca con `if (Multiplayer &amp;&amp; !photonView.IsMine) return;`
    ///                            o sea, solo funciona en la maquina del curado.
    ///   HealOther(int, bool)  -> si es mio llama a Heal; si no, manda
    ///                            photonView.RPC("HealOtherRPC", RpcTarget.All).
    ///   HealOtherRPC(...)     -> vuelve a filtrar por photonView.IsMine.
    ///
    /// Ese segundo filtro dentro de la RPC es la clave: aunque el evento llegue a
    /// todos, solo la maquina duena del jugador curado aplica la vida. Por eso
    /// HealOther se puede llamar desde CUALQUIER cliente sin duplicar la curacion,
    /// al reves que PlayerAvatar.Revive o ExtractionPoint.StateSet, que si hubo
    /// que meter detras de IsMasterClientOrSingleplayer().
    ///
    /// Resumen: el medico llama a HealOther en su propia maquina y ya esta. Cero
    /// eventos propios.
    /// </summary>
    internal static class RolMedico
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>    Enabled;
        internal static ConfigEntry<KeyCode> Tecla;
        internal static ConfigEntry<int>     Curacion;
        internal static ConfigEntry<int>     Cargas;
        internal static ConfigEntry<float>   MinutosRecarga;
        internal static ConfigEntry<float>   Alcance;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Medico", "Activado", true,
                "Mete el rol de medico en el sorteo.");

            Tecla = config.Bind(
                "Medico", "Tecla", KeyCode.G,
                "Tecla que cura al companero mas cercano que tengas a la vista.");

            Curacion = config.Bind(
                "Medico", "PuntosPorCura", 20,
                new ConfigDescription("Vida que devuelve cada carga.",
                    new AcceptableValueRange<int>(1, 200)));

            Cargas = config.Bind(
                "Medico", "Cargas", 3,
                new ConfigDescription("Curas disponibles a la vez.",
                    new AcceptableValueRange<int>(1, 10)));

            MinutosRecarga = config.Bind(
                "Medico", "MinutosRecarga", 3f,
                new ConfigDescription(
                    "Cada cuantos minutos se recupera UNA carga. Con 3 cargas y 3 minutos, " +
                    "gastarlas todas y volver a tenerlas llenas son 9 minutos.",
                    new AcceptableValueRange<float>(0.1f, 30f)));

            Alcance = config.Bind(
                "Medico", "Alcance", 4f,
                new ConfigDescription(
                    "Distancia maxima para curar a un companero, en metros.",
                    new AcceptableValueRange<float>(1f, 20f)));

            // La suscripcion va aqui y no en un constructor estatico: BindConfig se
            // llama una sola vez desde Plugin.Awake, asi que el momento es exacto y
            // no depende de cuando el runtime decida inicializar la clase.
            Roles.AlRepartir += () => { if (Roles.SoyEl(Rol.Medico)) Reiniciar(); };
        }

        // =====================================================================
        // Estado
        // =====================================================================

        private static int   _cargas;
        private static float _temporizador;

        internal static void Reiniciar()
        {
            _cargas       = Cargas.Value;
            _temporizador = MinutosRecarga.Value * 60f;
        }

        internal static void AvisarUso()
        {
            SemiFunc.UIFocusText(
                $"[{Tecla.Value}] curar  -  {_cargas}/{Cargas.Value} cargas de {Curacion.Value} PV",
                new Color(0.4f, 1f, 0.5f), Color.white, 5f);
        }

        // =====================================================================
        // Campos internos del juego (internal -> hay que ir por reflexion)
        // =====================================================================

        private static readonly FieldInfo _campoVida =
            AccessTools.Field(typeof(PlayerHealth), "health");

        private static readonly FieldInfo _campoVidaMax =
            AccessTools.Field(typeof(PlayerHealth), "maxHealth");

        private static readonly FieldInfo _campoDesactivado =
            AccessTools.Field(typeof(PlayerAvatar), "isDisabled");

        private static int Vida(PlayerHealth salud)
        {
            return _campoVida == null ? 0 : (int)_campoVida.GetValue(salud);
        }

        private static int VidaMax(PlayerHealth salud)
        {
            return _campoVidaMax == null ? 0 : (int)_campoVidaMax.GetValue(salud);
        }

        private static bool EstaMuerto(PlayerAvatar avatar)
        {
            return _campoDesactivado != null && (bool)_campoDesactivado.GetValue(avatar);
        }

        // =====================================================================
        // Tic
        // =====================================================================

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if (!Roles.SoyEl(Rol.Medico)) return;
            if (!Roles.EnPartida()) return;

            RecargarCargas();

            if (Roles.TeclaPulsada(Tecla.Value)) Curar();
        }

        private static void RecargarCargas()
        {
            if (_cargas >= Cargas.Value) return;

            _temporizador -= Time.deltaTime;
            if (_temporizador > 0f) return;

            _cargas++;
            _temporizador = MinutosRecarga.Value * 60f;

            SemiFunc.UIFocusText($"Cura recuperada  ({_cargas}/{Cargas.Value})",
                new Color(0.4f, 1f, 0.5f), Color.white, 3f);

            // Este log parece redundante con el mensaje de pantalla, pero no lo es:
            // sin el, la recarga es el unico camino del medico que no deja rastro
            // en el Player.log. Si un dia no regenera, desde el log no habria forma
            // de distinguir "el temporizador no corre" de "corre y el aviso no se
            // ve", que se arreglan en sitios muy distintos.
            Plugin.Debug($"Medico: carga recuperada, ahora {_cargas}/{Cargas.Value}. " +
                         $"Siguiente en {MinutosRecarga.Value * 60f:0} s.");
        }

        // =====================================================================
        // Curar
        // =====================================================================

        private static void Curar()
        {
            if (_cargas <= 0)
            {
                int faltan = Mathf.CeilToInt(_temporizador);
                SemiFunc.UIFocusText($"Sin cargas  -  {faltan / 60}:{(faltan % 60):00} para la siguiente",
                    new Color(1f, 0.5f, 0.3f), Color.white, 3f);
                return;
            }

            PlayerAvatar objetivo = BuscarHerido();
            if (objetivo == null)
            {
                SemiFunc.UIFocusText("No hay nadie herido cerca",
                    new Color(1f, 0.8f, 0.3f), Color.white, 2f);
                return;
            }

            // No se gasta carga hasta aqui: si no habia a quien curar, no se pierde.
            _cargas--;
            if (_cargas == Cargas.Value - 1)
                _temporizador = MinutosRecarga.Value * 60f;

            // HealOther se apana solo con la red (ver cabecera de la clase).
            objetivo.playerHealth.HealOther(Curacion.Value, true);

            SemiFunc.UIFocusText($"Curado  +{Curacion.Value} PV  ({_cargas}/{Cargas.Value})",
                new Color(0.4f, 1f, 0.5f), Color.white, 3f);

            Plugin.Debug($"Medico: curados {Curacion.Value} PV. Quedan {_cargas} cargas.");
        }

        /// <summary>
        /// El companero herido mas cercano dentro del alcance y con linea de vision.
        ///
        /// Se recorre la lista a mano en vez de usar
        /// SemiFunc.PlayerGetNearestPlayerAvatarWithinRange porque ese pide una
        /// LayerMask del juego y aqui nos interesa filtrar tambien por vida.
        /// </summary>
        private static PlayerAvatar BuscarHerido()
        {
            PlayerAvatar yo = PlayerAvatar.instance;
            if (yo == null) return null;

            List<PlayerAvatar> todos = SemiFunc.PlayerGetAll();
            if (todos == null) return null;

            Vector3 origen = yo.playerTransform != null
                ? yo.playerTransform.position
                : yo.transform.position;

            PlayerAvatar mejor    = null;
            float        mejorDis = float.MaxValue;

            foreach (PlayerAvatar otro in todos)
            {
                if (otro == null || otro == yo) continue;
                if (otro.playerHealth == null)  continue;
                if (EstaMuerto(otro))           continue;   // muerto: eso es revivir, no curar

                if (Vida(otro.playerHealth) >= VidaMax(otro.playerHealth)) continue;

                Vector3 destino = otro.playerTransform != null
                    ? otro.playerTransform.position
                    : otro.transform.position;

                float distancia = Vector3.Distance(origen, destino);
                if (distancia > Alcance.Value || distancia >= mejorDis) continue;

                mejor    = otro;
                mejorDis = distancia;
            }

            return mejor;
        }
    }
}
