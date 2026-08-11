using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// COMPROBACION DE VERSION ENTRE JUGADORES.
    ///
    /// Cada maquina anuncia su version del mod y avisa en pantalla si alguien de
    /// la sala lleva otra distinta, o directamente no lo lleva.
    ///
    /// ---------------------------------------------------------------------
    /// POR QUE EXISTE ESTO
    /// ---------------------------------------------------------------------
    /// Los dos fallos que mas tiempo han costado en este mod han sido el mismo
    /// fallo con dos caras: un desajuste invisible entre maquinas.
    ///
    ///   1. Arrancar desde Steam corre el juego en vanilla SIN avisar. El mod ni
    ///      se carga y no hay ningun indicio dentro del juego.
    ///   2. Entre la 1.2.0 y la 1.2.1 cambio el formato del reparto de roles.
    ///      Las dos versiones se descartan mutuamente con una comprobacion de
    ///      nulo, o sea que tampoco hay error: simplemente nadie recibe rol.
    ///
    /// Ninguno de los dos casos produce una excepcion, un aviso ni una linea roja
    /// en el log. Lo unico que se ve es que "el mod no hace nada", que es la peor
    /// pista posible. Esta clase convierte los dos en un mensaje explicito.
    ///
    /// Va a proposito FUERA del interruptor de los roles: sigue funcionando
    /// aunque [Roles] Activado = false, porque el desajuste afecta a todo el mod.
    ///
    /// Los clientes con 1.2.1 o anterior no anuncian nada, asi que apareceran
    /// como "sin el mod". Es correcto: son incompatibles igualmente, y el texto
    /// del aviso lo dice tal cual.
    /// </summary>
    internal static class ComprobacionDeVersion
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool> Enabled;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Version", "AvisarDesajuste", true,
                "Avisa en pantalla si algun jugador de la sala lleva otra version del mod " +
                "o no lo lleva. Muy recomendable dejarlo activado: un desajuste de version " +
                "no da ningun error, simplemente hace que los roles no funcionen.");
        }

        // =====================================================================
        // Estado
        // =====================================================================

        /// <summary>SteamID -> version anunciada.</summary>
        private static readonly Dictionary<string, string> _versiones = new Dictionary<string, string>();

        /// <summary>SteamID -> momento en que se le vio por primera vez en la sala.</summary>
        private static readonly Dictionary<string, float> _vistoDesde = new Dictionary<string, float>();

        private static float  _proximoAnuncio;
        private static float  _proximaRevision;
        private static string _ultimoProblema = "";

        /// <summary>
        /// Margen antes de dar a alguien por "sin mod". Un jugador que acaba de
        /// entrar todavia no ha anunciado nada, y acusarle en falso seria peor que
        /// no avisar.
        /// </summary>
        private const float MargenDeGracia = 25f;

        private const float CadaCuantoAnuncio  = 15f;
        private const float CadaCuantoRevision = 5f;

        internal static void AlCambiarNivel()
        {
            _versiones.Clear();
            _vistoDesde.Clear();
            _proximoAnuncio  = 2f;
            _proximaRevision = MargenDeGracia;
            _ultimoProblema  = "";
        }

        // =====================================================================
        // Tic
        // =====================================================================

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if (!Roles.EnPartida()) return;

            _proximoAnuncio -= Time.deltaTime;
            if (_proximoAnuncio <= 0f)
            {
                _proximoAnuncio = CadaCuantoAnuncio;
                Anunciar();
            }

            _proximaRevision -= Time.deltaTime;
            if (_proximaRevision <= 0f)
            {
                _proximaRevision = CadaCuantoRevision;
                Revisar();
            }
        }

        // =====================================================================
        // Anunciar / recibir
        // =====================================================================

        private static void Anunciar()
        {
            PlayerAvatar yo = PlayerAvatar.instance;
            if (yo == null) return;

            string id = SemiFunc.PlayerGetSteamID(yo);
            if (string.IsNullOrEmpty(id)) return;

            _versiones[id] = Plugin.PluginVersion;

            if (!SemiFunc.IsMultiplayer()) return;
            if (PhotonNetwork.NetworkingClient == null || !PhotonNetwork.InRoom) return;

            // Se repite cada 15 s en vez de una sola vez al entrar: asi los que
            // llegan tarde se enteran de las versiones de los que ya estaban, sin
            // tener que engancharse a los eventos de conexion de Photon.
            PhotonNetwork.RaiseEvent(
                Roles.EventoVersion,
                id + "|" + Plugin.PluginVersion,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable);
        }

        internal static void Recibir(object datos)
        {
            string texto = datos as string;
            if (texto == null) return;

            int corte = texto.IndexOf('|');
            if (corte <= 0 || corte == texto.Length - 1) return;

            _versiones[texto.Substring(0, corte)] = texto.Substring(corte + 1);
        }

        // =====================================================================
        // Revision
        // =====================================================================

        private static readonly FieldInfo _campoNombre =
            AccessTools.Field(typeof(PlayerAvatar), "playerName");

        private static string Nombre(PlayerAvatar avatar)
        {
            if (_campoNombre == null) return "Alguien";
            string n = _campoNombre.GetValue(avatar) as string;
            return string.IsNullOrEmpty(n) ? "Alguien" : n;
        }

        private static void Revisar()
        {
            if (!SemiFunc.IsMultiplayer()) return;

            List<PlayerAvatar> todos = SemiFunc.PlayerGetAll();
            if (todos == null || todos.Count <= 1) return;

            List<string> distintos = new List<string>();
            List<string> ausentes  = new List<string>();

            foreach (PlayerAvatar p in todos)
            {
                if (p == null) continue;

                string id = SemiFunc.PlayerGetSteamID(p);
                if (string.IsNullOrEmpty(id)) continue;

                if (!_vistoDesde.ContainsKey(id)) _vistoDesde[id] = Time.time;

                string version;
                if (_versiones.TryGetValue(id, out version))
                {
                    if (version != Plugin.PluginVersion)
                        distintos.Add($"{Nombre(p)} lleva la {version}");
                }
                else if (Time.time - _vistoDesde[id] >= MargenDeGracia)
                {
                    ausentes.Add(Nombre(p));
                }
            }

            if (distintos.Count == 0 && ausentes.Count == 0)
            {
                _ultimoProblema = "";
                return;
            }

            // Se avisa una vez por problema, no cada 5 segundos. Si entra alguien
            // nuevo con otra version, la firma cambia y vuelve a avisar.
            string firma = string.Join(",", distintos.ToArray()) + "//" + string.Join(",", ausentes.ToArray());
            if (firma == _ultimoProblema) return;
            _ultimoProblema = firma;

            Avisar(distintos, ausentes);
        }

        private static void Avisar(List<string> distintos, List<string> ausentes)
        {
            SemiFunc.UIBigMessage("MOD DESINCRONIZADO", "!", 35f,
                new Color(1f, 0.4f, 0.2f), Color.white);

            string detalle;
            if (distintos.Count > 0 && ausentes.Count > 0)
                detalle = $"{string.Join(", ", distintos.ToArray())}  -  " +
                          $"sin el mod: {string.Join(", ", ausentes.ToArray())}";
            else if (distintos.Count > 0)
                detalle = $"{string.Join(", ", distintos.ToArray())}  -  tu la {Plugin.PluginVersion}";
            else
                detalle = $"Sin el mod (o version anterior a la 1.3.0): {string.Join(", ", ausentes.ToArray())}";

            SemiFunc.UIFocusText(detalle, new Color(1f, 0.4f, 0.2f), Color.white, 8f);

            Plugin.Log.LogWarning(
                $"DESAJUSTE DE VERSION. Yo llevo la {Plugin.PluginVersion}. {detalle}. " +
                "Los roles no van a funcionar bien hasta que todos tengan la misma DLL. " +
                "Recordad que arrancar desde Steam corre el juego en vanilla sin avisar.");
        }
    }
}
