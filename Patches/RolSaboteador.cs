using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// ROL: SABOTEADOR.
    ///
    /// Apaga las luces del nivel durante unos minutos. Mientras dura el apagon la
    /// generacion de enemigos se acelera. La habilidad tiene un tiempo de recarga
    /// largo, asi que es un boton de "que empiece el caos", no un juguete.
    ///
    /// =====================================================================
    /// LAS DOS TRAMPAS DEL APAGON
    /// =====================================================================
    ///
    /// TRAMPA 1 - LightManager.turnOffLights NO es un interruptor.
    ///
    ///   Es el pestillo del apagon de vanilla. LightManager.Update hace:
    ///
    ///       if (RoundDirector.instance.allExtractionPointsCompleted &amp;&amp; !turnOffLights) {
    ///           StopAllCoroutines();
    ///           turningOffLights = true;    StartCoroutine(TurnOffLights());
    ///           turningOffEmissions = true; StartCoroutine(TurnOffEmissions());
    ///           turnOffLights = true;
    ///       }
    ///
    ///   O sea: se pone a true UNA vez, cuando ya has completado todas las
    ///   extracciones, para dejarte volver al camion a oscuras. Ponerlo a true
    ///   nosotros no apagaria nada; lo unico que conseguiriamos es CANCELAR el
    ///   apagon final del juego. Y no hay ninguna via para volver a encender.
    ///
    /// TRAMPA 2 - PropLight.SetIntensity(x) machaca originalIntensity.
    ///
    ///       public void SetIntensity(float intensity) {
    ///           lightComponent.intensity = intensity;
    ///           originalIntensity = intensity;      // &lt;-- aqui
    ///       }
    ///
    ///   Apagar con SetIntensity(0) destruye el unico dato con el que podriamos
    ///   restaurar la luz despues. El apagon seria permanente y ademas romperia
    ///   el fundido por distancia del propio LightManager.
    ///
    /// SOLUCION: tocamos lightComponent.enabled, que no lo usa nadie mas.
    ///   - no toca intensity, asi que el culling por distancia sigue con su
    ///     contabilidad intacta y al reactivar la luz vuelve como estaba
    ///   - solo apuntamos las luces que estaban encendidas en ese momento, para
    ///     no encender al restaurar las que el culling tenia apagadas a proposito
    ///
    /// Las linternas de los jugadores son FlashlightController, no PropLight, asi
    /// que sobreviven al apagon. Justo lo que se quiere: oscuridad total salvo tu
    /// linterna.
    ///
    /// =====================================================================
    /// RED
    /// =====================================================================
    /// LightManager es puramente local y no manda un solo RPC, asi que el apagon
    /// hay que difundirlo a mano (evento 177) o solo lo veria el saboteador.
    ///
    /// EnemyDirector, al reves, solo obedece al master:
    /// SetInvestigate/DisableDecrease se ignoran en los clientes. Por eso la
    /// subida de enemigos va por evento al master (176).
    /// </summary>
    internal static class RolSaboteador
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>    Enabled;
        internal static ConfigEntry<KeyCode> Tecla;
        internal static ConfigEntry<float>   MinutosApagon;
        internal static ConfigEntry<float>   MinutosRecarga;
        internal static ConfigEntry<bool>    SubirEnemigos;
        internal static ConfigEntry<float>   FuerzaEnemigos;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Saboteador", "Activado", true,
                "Mete el rol de saboteador en el sorteo.");

            Tecla = config.Bind(
                "Saboteador", "Tecla", KeyCode.H,
                "Tecla que dispara el apagon.");

            MinutosApagon = config.Bind(
                "Saboteador", "MinutosApagon", 2f,
                new ConfigDescription("Cuanto duran las luces apagadas.",
                    new AcceptableValueRange<float>(0.1f, 15f)));

            MinutosRecarga = config.Bind(
                "Saboteador", "MinutosRecarga", 15f,
                new ConfigDescription(
                    "Recarga de la habilidad, contada desde que EMPIEZA el apagon.",
                    new AcceptableValueRange<float>(0.1f, 60f)));

            SubirEnemigos = config.Bind(
                "Saboteador", "SubirEnemigos", true,
                "Durante el apagon acelera la aparicion de enemigos.");

            FuerzaEnemigos = config.Bind(
                "Saboteador", "FuerzaEnemigos", 10f,
                new ConfigDescription(
                    "Segundos que se le restan al contador de aparicion de cada enemigo " +
                    "dormido, una vez por segundo mientras dura el apagon. El juego usa 5 " +
                    "para un ruido normal, asi que 10 es aproximadamente el doble de rapido.",
                    new AcceptableValueRange<float>(1f, 60f)));

            Roles.AlRepartir += () => { if (Roles.SoyEl(Rol.Saboteador)) Reiniciar(); };
        }

        // =====================================================================
        // Estado del saboteador (solo en su maquina)
        // =====================================================================

        private static float _recarga;

        internal static void Reiniciar()
        {
            _recarga = 0f;   // empieza la partida con la habilidad lista
        }

        /// <summary>Como se usa el rol. Ver el comentario en RolMedico.TextoDeUso.</summary>
        internal static string TextoDeUso()
        {
            return $"[{Tecla.Value}] apagon de {MinutosApagon.Value:0} min  -  recarga {MinutosRecarga.Value:0} min";
        }

        internal static void AvisarUso()
        {
            SemiFunc.UIFocusText(TextoDeUso(), Roles.ColorSaboteador, Color.white, 5f);
        }

        // =====================================================================
        // Tic
        // =====================================================================

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if (!Roles.EnPartida()) return;

            // El apagon lo mantiene TODA maquina que lo haya recibido, sea o no el
            // saboteador. Va antes del filtro de rol a proposito.
            MantenerApagon();

            // El boost de enemigos solo lo lleva el master.
            if (SemiFunc.IsMasterClientOrSingleplayer()) MantenerBoost();

            if (!Roles.SoyEl(Rol.Saboteador)) return;

            if (_recarga > 0f) _recarga -= Time.deltaTime;

            if (Roles.TeclaPulsada(Tecla.Value)) Sabotear();
        }

        private static void Sabotear()
        {
            if (_recarga > 0f)
            {
                int faltan = Mathf.CeilToInt(_recarga);
                SemiFunc.UIFocusText($"Recargando  -  {faltan / 60}:{(faltan % 60):00}",
                    new Color(1f, 0.5f, 0.3f), Color.white, 3f);
                return;
            }

            float segundos = MinutosApagon.Value * 60f;
            _recarga = MinutosRecarga.Value * 60f;

            // Nosotros nos lo aplicamos directamente y lo mandamos a los demas, en
            // vez de fiarlo todo al eco del servidor.
            IniciarApagon(segundos);

            if (SemiFunc.IsMultiplayer() && PhotonNetwork.NetworkingClient != null)
            {
                PhotonNetwork.RaiseEvent(
                    Roles.EventoApagon,
                    segundos,
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                    SendOptions.SendReliable);
            }

            if (SubirEnemigos.Value) PedirBoost(segundos);

            SemiFunc.UIFocusText("SABOTAJE", new Color(1f, 0.2f, 0.2f), Color.white, 3f);
            Plugin.Log.LogInfo($"Saboteador: apagon de {segundos:0} s lanzado.");
        }

        // =====================================================================
        // Apagon
        // =====================================================================

        private static readonly FieldInfo _campoPropLights =
            AccessTools.Field(typeof(LightManager), "propLights");

        private static readonly FieldInfo _campoLuz =
            AccessTools.Field(typeof(PropLight), "lightComponent");

        private static float _apagonRestante;
        private static float _reaplicarEn;

        /// <summary>Luces que apagamos NOSOTROS y que hay que volver a encender.</summary>
        private static readonly List<Light> _apagadas = new List<Light>();

        internal static void EjecutarApagon(object datos)
        {
            if (!(datos is float)) return;
            IniciarApagon((float)datos);
        }

        private static void IniciarApagon(float segundos)
        {
            _apagonRestante = segundos;
            _reaplicarEn    = 0f;
            ApagarLoQueHaya();
        }

        private static void MantenerApagon()
        {
            if (_apagonRestante <= 0f) return;

            _apagonRestante -= Time.deltaTime;

            if (_apagonRestante <= 0f)
            {
                Encender();
                return;
            }

            // Se reaplica cada medio segundo: durante el apagon puede aparecer
            // material nuevo (habitaciones que se cargan, lamparas rotas que se
            // reactivan) y esas luces llegarian encendidas.
            _reaplicarEn -= Time.deltaTime;
            if (_reaplicarEn <= 0f)
            {
                _reaplicarEn = 0.5f;
                ApagarLoQueHaya();
            }
        }

        private static void ApagarLoQueHaya()
        {
            IList lista = ListaDeLuces();
            if (lista == null) return;

            foreach (object propLight in lista)
            {
                Light luz = LuzDe(propLight);
                if (luz == null || !luz.enabled) continue;

                luz.enabled = false;
                _apagadas.Add(luz);
            }
        }

        private static void Encender()
        {
            foreach (Light luz in _apagadas)
                if (luz != null) luz.enabled = true;

            int cuantas = _apagadas.Count;
            _apagadas.Clear();
            _apagonRestante = 0f;

            Plugin.Debug($"Saboteador: apagon terminado, {cuantas} luces devueltas.");
        }

        private static IList ListaDeLuces()
        {
            if (LightManager.instance == null || _campoPropLights == null) return null;
            return _campoPropLights.GetValue(LightManager.instance) as IList;
        }

        private static Light LuzDe(object propLight)
        {
            if (propLight == null || _campoLuz == null) return null;
            return _campoLuz.GetValue(propLight) as Light;
        }

        // =====================================================================
        // Subida de enemigos (solo master)
        // =====================================================================

        private static readonly FieldInfo _campoEnemigos =
            AccessTools.Field(typeof(EnemyDirector), "enemiesSpawned");

        private static readonly FieldInfo _campoSpawned =
            AccessTools.Field(typeof(EnemyParent), "Spawned");

        private static float _boostRestante;
        private static float _boostSiguiente;

        private static void PedirBoost(float segundos)
        {
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                _boostRestante  = segundos;
                _boostSiguiente = 0f;
                return;
            }

            if (PhotonNetwork.NetworkingClient == null) return;

            PhotonNetwork.RaiseEvent(
                Roles.EventoSabotaje,
                segundos,
                new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
                SendOptions.SendReliable);

            Plugin.Debug("Saboteador: no soy master, pedido el boost de enemigos.");
        }

        internal static void EjecutarBoostEnemigos(object datos)
        {
            if (!(datos is float)) return;
            _boostRestante  = (float)datos;
            _boostSiguiente = 0f;
        }

        /// <summary>
        /// Una vez por segundo le resta tiempo al contador de aparicion de cada
        /// enemigo que todavia no ha salido.
        ///
        /// EnemyParent.DisableDecrease(float) es la palanca que usa el propio juego
        /// en EnemyDirector.SetInvestigate cuando un ruido es lo bastante gordo:
        ///
        ///     if (!enemyParent.Spawned) { if (radius >= 15f) enemyParent.DisableDecrease(5f); }
        ///
        /// Aqui se hace lo mismo pero sostenido en el tiempo, que es exactamente
        /// "durante el apagon aparecen mas bichos".
        /// </summary>
        private static void MantenerBoost()
        {
            if (_boostRestante <= 0f) return;

            _boostRestante -= Time.deltaTime;

            _boostSiguiente -= Time.deltaTime;
            if (_boostSiguiente > 0f) return;
            _boostSiguiente = 1f;

            if (EnemyDirector.instance == null || _campoEnemigos == null) return;

            IList enemigos = _campoEnemigos.GetValue(EnemyDirector.instance) as IList;
            if (enemigos == null) return;

            int empujados = 0;

            foreach (object obj in enemigos)
            {
                EnemyParent padre = obj as EnemyParent;
                if (padre == null) continue;

                // Ya esta fuera: no hay nada que acelerar.
                if (_campoSpawned != null && (bool)_campoSpawned.GetValue(padre)) continue;

                padre.DisableDecrease(FuerzaEnemigos.Value);
                empujados++;
            }

            if (empujados > 0)
                Plugin.Debug($"Saboteador: {empujados} enemigos acelerados ({_boostRestante:0} s restantes).");
        }
    }
}
