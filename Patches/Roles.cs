using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RepoAmigos.Patches
{
    internal enum Rol : byte
    {
        Ninguno    = 0,
        Medico     = 1,
        Ingeniero  = 2,
        Saboteador = 3,
        Rastreador = 4
    }

    /// <summary>
    /// ESPINA DORSAL DE LOS ROLES (estilo Among Us).
    ///
    /// Reparte un rol secreto a algunos jugadores al empezar cada nivel y ofrece
    /// a los tres roles el sitio comun donde apoyarse: el reparto, el tic por
    /// frame, la lectura de teclas y el canal de red.
    ///
    /// ---------------------------------------------------------------------
    /// POR QUE EL TIC VA EN RunManager.Update Y NO EN Plugin.Update
    /// ---------------------------------------------------------------------
    /// Unity destruye el componente del plugin al cambiar de escena (por eso el
    /// OnDestroy de Plugin.cs salta al entrar a un nivel). Si el temporizador de
    /// recarga del medico viviera en Plugin.Update, dejaria de correr en cuanto
    /// empieza la primera partida y las cargas no se regenerarian jamas, sin un
    /// solo error en el log.
    ///
    /// Los parches Harmony si sobreviven al cambio de escena, asi que el tic va
    /// colgado de un Postfix sobre RunManager.Update: singleton, un solo objeto,
    /// una llamada por frame.
    ///
    /// ---------------------------------------------------------------------
    /// IDENTIDAD DEL JUGADOR
    /// ---------------------------------------------------------------------
    /// Los roles se guardan por SteamID (SemiFunc.PlayerGetSteamID), no por
    /// referencia al PlayerAvatar: entre niveles no hay garantia de que el objeto
    /// siga siendo el mismo, pero el SteamID si.
    ///
    /// ---------------------------------------------------------------------
    /// RED
    /// ---------------------------------------------------------------------
    /// No se puede anadir un [PunRPC] a una clase del juego desde fuera, asi que
    /// vamos con RaiseEvent y codigos propios, igual que ExtractionOnDemand con
    /// su 174. El rango 1..199 es libre; PUN se reserva del 200 en adelante.
    ///
    ///   175  master -> todos     reparto de roles
    ///   176  cliente -> master   subir la generacion de enemigos durante el
    ///                            sabotaje (EnemyDirector solo obedece al master)
    ///   177  cualquiera -> todos apagon (LightManager es local en cada maquina y
    ///                            no manda un solo RPC por su cuenta)
    /// </summary>
    internal static class Roles
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>    Enabled;
        internal static ConfigEntry<int>     MinJugadores;
        internal static ConfigEntry<bool>    RepartirCadaNivel;
        internal static ConfigEntry<bool>    AnunciarRol;
        internal static ConfigEntry<bool>    RecordarRol;
        internal static ConfigEntry<KeyCode> TeclaRecordar;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Roles", "Activado", true,
                "Reparte roles secretos (medico, ingeniero, saboteador, rastreador) al empezar " +
                "cada nivel. Cada rol se activa ademas por su cuenta en su propia seccion.");

            MinJugadores = config.Bind(
                "Roles", "MinimoJugadores", 3,
                new ConfigDescription(
                    "Por debajo de esta cantidad de jugadores no se reparte ningun rol. " +
                    "Con dos personas un saboteador no tiene gracia.",
                    new AcceptableValueRange<int>(1, 20)));

            RepartirCadaNivel = config.Bind(
                "Roles", "RepartirCadaNivel", true,
                "true: se vuelven a sortear los roles en cada nivel, asi todos van rotando. " +
                "false: se sortean una vez y duran toda la partida.");

            AnunciarRol = config.Bind(
                "Roles", "AnunciarRol", true,
                "Muestra en pantalla que rol te ha tocado al empezar el nivel. " +
                "Solo lo ves tu: el reparto de los demas no se anuncia a nadie.");

            RecordarRol = config.Bind(
                "Roles", "RecordarRol", true,
                "Permite volver a ver en cualquier momento que rol tienes y como se usa, " +
                "pulsando la tecla de abajo. El aviso del principio del nivel se pierde " +
                "enseguida y luego no habia forma de consultarlo.");

            TeclaRecordar = config.Bind(
                "Roles", "TeclaRecordar", KeyCode.Tab,
                "Tecla que recuerda tu rol. Tab es tambien la del mapa del juego, asi que " +
                "las dos cosas salen a la vez; no se pisan, pero se puede cambiar aqui.");
        }

        // =====================================================================
        // Estado del reparto
        // =====================================================================

        private static readonly Dictionary<string, Rol> _asignados = new Dictionary<string, Rol>();

        private static Rol   _local          = Rol.Ninguno;
        private static bool  _repartoHecho;
        private static bool  _pendienteRepartir;
        private static float _esperaReparto;
        private static float _esperaAnuncio = -1f;

        /// <summary>Rol del jugador de esta maquina. Rol.Ninguno si no le toco nada.</summary>
        internal static Rol Local
        {
            get { return Enabled != null && Enabled.Value ? _local : Rol.Ninguno; }
        }

        internal static bool SoyEl(Rol rol)
        {
            return rol != Rol.Ninguno && Local == rol;
        }

        /// <summary>
        /// true si ese rol lo tiene alguien que SIGUE EN LA SALA.
        ///
        /// Lo usa el ingeniero: si no hay ingeniero (pocos jugadores, rol apagado
        /// en el .cfg...), el pato tiene que volver a funcionar para todo el mundo.
        /// Bloquear la extraccion del grupo entero por un rol que no existe seria
        /// dejar la partida muerta.
        ///
        /// No basta con mirar si el rol esta en la tabla: hay que comprobar que su
        /// dueno sigue conectado. Si el ingeniero se desconecta a mitad de nivel,
        /// su SteamID se queda en el diccionario, esto devolveria true y el pato
        /// dejaria de responderle a NADIE. Nivel bloqueado sin salida y sin ninguna
        /// pista de por que: el boton simplemente no hace nada.
        ///
        /// Se recorre la lista de jugadores en cada llamada, pero esto solo se
        /// invoca al pulsar el boton, no por frame.
        /// </summary>
        internal static bool RolEnJuego(Rol rol)
        {
            if (Enabled == null || !Enabled.Value) return false;

            List<PlayerAvatar> presentes = SemiFunc.PlayerGetAll();
            if (presentes == null || presentes.Count == 0) return false;

            foreach (var par in _asignados)
            {
                if (par.Value != rol) continue;

                foreach (PlayerAvatar p in presentes)
                {
                    if (p == null) continue;
                    if (SemiFunc.PlayerGetSteamID(p) == par.Key) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Cada rol se suscribe para reiniciar sus cargas y temporizadores cuando
        /// se reparte de nuevo.
        /// </summary>
        internal static event System.Action AlRepartir;

        // =====================================================================
        // Cambio de nivel: marcar que toca repartir
        // =====================================================================

        [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
        private static class MarcarRepartoAlCambiarNivel
        {
            private static void Postfix()
            {
                // Tambien fuera del interruptor de roles, por lo mismo de arriba.
                ComprobacionDeVersion.AlCambiarNivel();

                if (Enabled == null || !Enabled.Value) return;

                if (_repartoHecho && !RepartirCadaNivel.Value)
                {
                    // Los roles duran toda la partida: solo hay que re-anunciarlos.
                    _esperaAnuncio = 4f;
                    return;
                }

                _pendienteRepartir = true;
                // Margen para que los PlayerAvatar existan y tengan SteamID.
                _esperaReparto = 3f;

                Plugin.Debug("Roles: cambio de nivel, reparto pendiente.");
            }
        }

        // =====================================================================
        // Tic por frame
        // =====================================================================

        [HarmonyPatch(typeof(RunManager), "Update")]
        private static class TicPorFrame
        {
            private static void Postfix()
            {
                // La red y la comprobacion de version van ANTES del interruptor de
                // los roles: un desajuste de version rompe el mod entero, asi que
                // hay que avisar aunque los roles esten apagados.
                AsegurarRed();
                ComprobacionDeVersion.Tick();

                // El recordatorio va ANTES del interruptor de los roles a proposito:
                // si estan apagados en el .cfg, pulsar la tecla tiene que decirlo en
                // vez de no hacer nada, que se lee como que el mod esta roto.
                if (RecordarRol != null && RecordarRol.Value
                    && EnPartida() && TeclaPulsada(TeclaRecordar.Value))
                    Recordar();

                if (Enabled == null || !Enabled.Value) return;

                if (_pendienteRepartir) IntentarRepartir();

                RevisarHuerfanos();
                RedifundirDeVezEnCuando();

                if (_esperaAnuncio > 0f)
                {
                    _esperaAnuncio -= Time.deltaTime;
                    if (_esperaAnuncio <= 0f)
                    {
                        _esperaAnuncio = -1f;
                        Anunciar();
                    }
                }

                // Los roles hacen su trabajo aqui. Cada uno comprueba por su cuenta
                // si le toca, si esta activado y si hay partida en marcha.
                RolMedico.Tick();
                RolIngeniero.Tick();
                RolSaboteador.Tick();
                RolRastreador.Tick();
            }
        }

        // =====================================================================
        // Reparto (solo lo ejecuta el master, y solo dentro de un nivel de verdad)
        // =====================================================================

        /// <summary>
        /// Filtra CUANDO se puede repartir. Los tres candados importan:
        ///
        /// 1. `RunIsLevel()` — nada de repartir en el menu, el lobby ni la tienda.
        ///    En la primera prueba real el reparto saltaba 3 s despues del
        ///    ChangeLevel al "Level - Lobby Menu", con la gente todavia entrando,
        ///    y salia vacio: en el log se ve un "me ha tocado Ninguno" antes
        ///    siquiera de cargar el Manor.
        ///
        /// 2. `PhotonNetwork.InRoom` — estar conectado no basta; hasta que no se
        ///    entra en la sala, PlayerGetAll devuelve una lista a medio llenar.
        ///
        /// 3. El candado de autoridad va EL ULTIMO y a proposito, porque
        ///    `SemiFunc.IsMasterClientOrSingleplayer()` empieza asi:
        ///
        ///        if (!GameManager.Multiplayer()) return true;
        ///
        ///    o sea que mientras el modo de juego no este fijado devuelve true en
        ///    TODAS las maquinas. Preguntarlo durante una transicion de nivel hace
        ///    que cada cliente se crea el anfitrion y reparta por su cuenta.
        ///
        /// Si todavia no toca, se sale sin consumir el aviso: se reintenta en el
        /// frame siguiente. Eso ademas cubre el cambio de anfitrion, porque quien
        /// herede el master acabara entrando por aqui.
        /// </summary>
        private static void IntentarRepartir()
        {
            _esperaReparto -= Time.deltaTime;
            if (_esperaReparto > 0f) return;

            if (RunManager.instance == null)   return;
            if (!SemiFunc.RunIsLevel())        return;
            if (PlayerAvatar.instance == null) return;

            if (SemiFunc.IsMultiplayer() && !PhotonNetwork.InRoom) return;

            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            Repartir();
        }

        private static void Repartir()
        {
            _pendienteRepartir = false;

            List<PlayerAvatar> jugadores = SemiFunc.PlayerGetAll();
            if (jugadores == null || jugadores.Count == 0)
            {
                // Aun no hay nadie: reintentar en el siguiente segundo.
                _pendienteRepartir = true;
                _esperaReparto     = 1f;
                return;
            }

            _asignados.Clear();

            if (jugadores.Count < MinJugadores.Value)
            {
                Plugin.Debug($"Roles: solo {jugadores.Count} jugadores, minimo {MinJugadores.Value}. Sin roles.");
                Difundir(true);
                Aplicar(true);
                return;
            }

            // Roles en juego, EN ORDEN DE PRIORIDAD.
            //
            // Cuando hay menos jugadores que roles, los ultimos de la lista se
            // quedan sin repartir.
            //
            // El ingeniero encabezaba la lista porque era el unico que podia activar
            // la extraccion, asi que sin el no se podia terminar el nivel. Eso ya no
            // es cierto: desde la 1.5.0 extrae cualquiera y el ingeniero solo recarga
            // baterias. Hoy los cuatro roles son ventajas y ninguno bloquea nada, asi
            // que este orden es simple preferencia y se puede cambiar sin miedo.
            List<Rol> bolsa = new List<Rol>();
            if (RolIngeniero.Enabled.Value)  bolsa.Add(Rol.Ingeniero);
            if (RolMedico.Enabled.Value)     bolsa.Add(Rol.Medico);
            if (RolSaboteador.Enabled.Value) bolsa.Add(Rol.Saboteador);
            if (RolRastreador.Enabled.Value) bolsa.Add(Rol.Rastreador);

            // Barajar a los jugadores (Fisher-Yates) y darle un rol a los primeros.
            List<PlayerAvatar> mezcla = new List<PlayerAvatar>(jugadores);
            for (int i = mezcla.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                PlayerAvatar tmp = mezcla[i];
                mezcla[i] = mezcla[j];
                mezcla[j] = tmp;
            }

            int repartidos = 0;
            for (int i = 0; i < mezcla.Count && repartidos < bolsa.Count; i++)
            {
                string id = SemiFunc.PlayerGetSteamID(mezcla[i]);
                if (string.IsNullOrEmpty(id)) continue;      // aun sin SteamID, se lo salta

                _asignados[id] = bolsa[repartidos];
                repartidos++;
            }

            _repartoHecho = true;

            Plugin.Debug($"Roles: repartidos {repartidos} de {bolsa.Count} entre {jugadores.Count} jugadores.");

            Difundir(true);
            Aplicar(true);
        }

        /// <summary>
        /// Pasa el diccionario a la variable local y avisa a los roles.
        /// </summary>
        /// <param name="repartoNuevo">
        /// true para un sorteo de nivel nuevo: siempre reinicia cargas y anuncia.
        /// false para una reparacion a mitad de nivel (alguien se desconecto y su
        /// rol se reasigna); ahi solo se reinicia y se anuncia si TU rol ha
        /// cambiado. Si no se distinguiera, reasignar el rol de un jugador que se
        /// va le devolveria al medico las 3 cargas y le quitaria la recarga al
        /// saboteador, a todos, cada vez que alguien abandona la partida.
        /// </param>
        private static void Aplicar(bool repartoNuevo)
        {
            Rol anterior = _local;
            _local = Rol.Ninguno;

            PlayerAvatar yo = PlayerAvatar.instance;
            if (yo != null)
            {
                string id = SemiFunc.PlayerGetSteamID(yo);
                Rol mio;
                if (!string.IsNullOrEmpty(id) && _asignados.TryGetValue(id, out mio))
                    _local = mio;
            }

            bool cambio = _local != anterior;

            if (repartoNuevo || cambio)
                Plugin.Log.LogInfo($"Roles: me ha tocado {_local}.");

            if ((repartoNuevo || cambio) && AlRepartir != null) AlRepartir();

            if (repartoNuevo || cambio)
                _esperaAnuncio = AnunciarRol.Value ? 2f : -1f;
        }

        private static void Anunciar()
        {
            switch (_local)
            {
                case Rol.Medico:
                    SemiFunc.UIBigMessage("ERES EL MEDICO", "+", 40f,
                        new Color(0.4f, 1f, 0.5f), Color.white);
                    RolMedico.AvisarUso();
                    break;

                case Rol.Ingeniero:
                    SemiFunc.UIBigMessage("ERES EL INGENIERO", "*", 40f,
                        new Color(0.4f, 0.8f, 1f), Color.white);
                    RolIngeniero.AvisarUso();
                    break;

                case Rol.Saboteador:
                    SemiFunc.UIBigMessage("ERES EL SABOTEADOR", "!", 40f,
                        new Color(1f, 0.3f, 0.3f), Color.white);
                    RolSaboteador.AvisarUso();
                    break;

                case Rol.Rastreador:
                    SemiFunc.UIBigMessage("ERES EL RASTREADOR", "?", 40f,
                        new Color(1f, 0.85f, 0.3f), Color.white);
                    RolRastreador.AvisarUso();
                    break;
            }
        }

        /// <summary>
        /// Vuelve a mostrar el rol y como se usa, a peticion del jugador.
        ///
        /// Reutiliza Anunciar() en vez de repetir los textos: asi el recordatorio
        /// no puede quedarse desfasado respecto al aviso del principio del nivel, y
        /// los AvisarUso() de cada rol traen el estado del momento — el del medico,
        /// por ejemplo, dice cuantas cargas le quedan AHORA, no cuantas empezo.
        ///
        /// Lo unico propio es la rama de "sin rol". Sin ella, quien no tiene rol
        /// pulsaria la tecla y no pasaria nada, que es indistinguible de que el mod
        /// no funcione; y el motivo casi nunca es evidente (falta gente en la sala,
        /// o el reparto de este nivel no le toco).
        /// </summary>
        private static void Recordar()
        {
            if (Enabled != null && Enabled.Value && _local != Rol.Ninguno)
            {
                Anunciar();
                return;
            }

            Color gris = new Color(0.75f, 0.75f, 0.75f);
            string motivo;

            if (Enabled == null || !Enabled.Value)
            {
                motivo = "Los roles estan desactivados en la configuracion";
            }
            else if (!_repartoHecho)
            {
                motivo = "Todavia no se han repartido los roles de este nivel";
            }
            else
            {
                List<PlayerAvatar> presentes = SemiFunc.PlayerGetAll();
                int cuantos = presentes == null ? 0 : presentes.Count;

                motivo = cuantos < MinJugadores.Value
                    ? $"Hacen falta {MinJugadores.Value} jugadores y sois {cuantos}"
                    : "Este nivel no te ha tocado ningun rol";
            }

            SemiFunc.UIBigMessage("SIN ROL", "-", 40f, gris, Color.white);
            SemiFunc.UIFocusText(motivo, gris, Color.white, 4f);
        }

        // =====================================================================
        // Lectura de teclas
        //
        // REPO trae los dos sistemas de entrada. Con el Input System nuevo activo,
        // el UnityEngine.Input clasico puede quedarse inerte SIN lanzar excepcion:
        // devuelve false siempre. Por eso se prueban los dos, igual que en
        // GeneradorDePruebas.
        // =====================================================================

        internal static bool TeclaPulsada(KeyCode tecla)
        {
            try
            {
                Keyboard teclado = Keyboard.current;
                Key codigo;
                if (teclado != null && System.Enum.TryParse(tecla.ToString(), true, out codigo))
                {
                    var control = teclado[codigo];
                    if (control != null && control.wasPressedThisFrame) return true;
                }
            }
            catch { /* el Input System nuevo puede no estar disponible */ }

            try
            {
                if (Input.GetKeyDown(tecla)) return true;
            }
            catch { /* lanza si el proyecto usa solo el Input System nuevo */ }

            return false;
        }

        /// <summary>
        /// true si estamos dentro de un nivel jugable de verdad.
        ///
        /// `RunIsLevel()` descarta el menu principal, el lobby, el lobby menu y la
        /// tienda de una sola llamada. Antes esto miraba `!MenuLevel()`, que deja
        /// pasar la tienda y el lobby: alli las habilidades no deben funcionar.
        /// </summary>
        internal static bool EnPartida()
        {
            return RunManager.instance != null
                && PlayerAvatar.instance != null
                && SemiFunc.RunIsLevel();
        }

        // =====================================================================
        // Red
        // =====================================================================

        internal const byte EventoReparto  = 175;
        internal const byte EventoSabotaje = 176;
        internal const byte EventoApagon   = 177;
        internal const byte EventoVersion  = 178;
        internal const byte EventoRecarga  = 179;

        private static bool _redConectada;

        private static void AsegurarRed()
        {
            if (_redConectada) return;
            if (PhotonNetwork.NetworkingClient == null) return;   // PUN aun no esta listo

            // Se desengancha ANTES de enganchar. Quitar un delegado que no estaba
            // suscrito no hace nada, pero suscribir dos veces el mismo si: cada
            // evento se procesaria por duplicado.
            //
            // Y es un caso que ocurre de verdad: DesconectarRed pone
            // _redConectada = false aunque NetworkingClient sea null en ese
            // instante, o sea sin llegar a desuscribir. Como Plugin.OnDestroy
            // salta en cada cambio de escena, sin esta linea los manejadores se
            // irian acumulando durante la partida.
            PhotonNetwork.NetworkingClient.EventReceived -= AlRecibirEvento;
            PhotonNetwork.NetworkingClient.EventReceived += AlRecibirEvento;

            _redConectada = true;
            Plugin.Debug("Roles: enganchado al canal de eventos de Photon.");
        }

        internal static void DesconectarRed()
        {
            if (!_redConectada) return;
            if (PhotonNetwork.NetworkingClient != null)
                PhotonNetwork.NetworkingClient.EventReceived -= AlRecibirEvento;
            _redConectada = false;
        }

        /// <summary>
        /// Manejador de eventos de Photon.
        ///
        /// TODO el cuerpo va dentro de un try/catch, y no es por pereza: esto lo
        /// invoca PUN desde su propio bucle de despacho de mensajes. Una excepcion
        /// que se escape de aqui sube por la pila de la libreria de red, no por la
        /// nuestra, y puede tumbarle la conexion al jugador — que desde fuera se
        /// ve como "se ha caido el servidor", sin ninguna pista que apunte al mod.
        ///
        /// Un rol que falla tiene que ser un rol que no funciona, nunca un
        /// jugador expulsado de la partida.
        /// </summary>
        private static void AlRecibirEvento(EventData datos)
        {
            try
            {
                switch (datos.Code)
                {
                    case EventoReparto:
                        RecibirReparto(datos.CustomData);
                        break;

                    case EventoSabotaje:
                        // Solo el master manda sobre los enemigos.
                        if (SemiFunc.IsMasterClient())
                            RolSaboteador.EjecutarBoostEnemigos(datos.CustomData);
                        break;

                    case EventoApagon:
                        RolSaboteador.EjecutarApagon(datos.CustomData);
                        break;

                    case EventoVersion:
                        ComprobacionDeVersion.Recibir(datos.CustomData);
                        break;

                    case EventoRecarga:
                        // Solo el anfitrion puede propagar un cambio de bateria; ver
                        // la cabecera de RolIngeniero.
                        if (SemiFunc.IsMasterClient())
                            RolIngeniero.EjecutarRecargaRemota(datos.CustomData);
                        break;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Roles: fallo procesando el evento {datos.Code}. " +
                                    $"Se ignora para no tumbar la conexion.\n{e}");
            }
        }

        // =====================================================================
        // Reparacion: un rol nunca puede quedarse sin dueno presente
        // =====================================================================

        private static float _proximaRevisionHuerfanos = 5f;

        /// <summary>
        /// Si el dueno de un rol se desconecta, el anfitrion se lo pasa a alguien
        /// que siga en la sala.
        ///
        /// Esto no es cosmetico: sin ello, que el INGENIERO abandone la partida
        /// deja el nivel sin salida. Su SteamID se queda en el diccionario, nadie
        /// presente es el ingeniero, y el boton de extraccion deja de responderle
        /// a nadie — sin mensaje, sin error, sin ninguna pista. El grupo se queda
        /// encerrado sin poder terminar el nivel.
        ///
        /// Se reasignan SOLO los roles huerfanos, a jugadores presentes que no
        /// tengan ninguno. Volver a sortear todo seria mas simple pero le cambiaria
        /// el rol a gente que no tiene nada que ver, a mitad de partida.
        ///
        /// Si no hay nadie libre a quien darselo (por ejemplo quedan 2 jugadores y
        /// ambos ya tienen rol), el huerfano se borra igualmente: es preferible que
        /// el rol no exista — y entonces el boton vuelve a ser de todos — a que
        /// exista a nombre de un fantasma.
        /// </summary>
        private static void RevisarHuerfanos()
        {
            if (!_repartoHecho) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!EnPartida()) return;

            _proximaRevisionHuerfanos -= Time.deltaTime;
            if (_proximaRevisionHuerfanos > 0f) return;
            _proximaRevisionHuerfanos = 5f;

            List<PlayerAvatar> presentes = SemiFunc.PlayerGetAll();
            if (presentes == null || presentes.Count == 0) return;

            // SteamIDs que siguen en la sala.
            List<string> ids = new List<string>();
            foreach (PlayerAvatar p in presentes)
            {
                if (p == null) continue;
                string id = SemiFunc.PlayerGetSteamID(p);
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            if (ids.Count == 0) return;

            // Roles cuyo dueno ya no esta.
            List<string> fantasmas = new List<string>();
            List<Rol>    huerfanos = new List<Rol>();
            foreach (var par in _asignados)
            {
                if (ids.Contains(par.Key)) continue;
                fantasmas.Add(par.Key);
                huerfanos.Add(par.Value);
            }
            if (fantasmas.Count == 0) return;

            foreach (string f in fantasmas) _asignados.Remove(f);

            // Presentes que se han quedado sin rol.
            List<string> libres = new List<string>();
            foreach (string id in ids)
                if (!_asignados.ContainsKey(id)) libres.Add(id);

            // Los huerfanos se reparten en el orden de prioridad de la bolsa, asi
            // que el ingeniero se recoloca antes que ningun otro.
            huerfanos.Sort((a, b) => Prioridad(a).CompareTo(Prioridad(b)));

            int dados = 0;
            for (int i = 0; i < huerfanos.Count && i < libres.Count; i++)
            {
                _asignados[libres[i]] = huerfanos[i];
                dados++;
            }

            Plugin.Log.LogInfo(
                $"Roles: {fantasmas.Count} rol(es) sin dueno tras una desconexion. " +
                $"Reasignados {dados}, descartados {huerfanos.Count - dados}.");

            Difundir(false);
            Aplicar(false);   // reparacion, no sorteo nuevo
        }

        /// <summary>Mismo orden que la bolsa de Repartir: el ingeniero primero.</summary>
        private static int Prioridad(Rol rol)
        {
            switch (rol)
            {
                case Rol.Ingeniero:  return 0;
                case Rol.Medico:     return 1;
                case Rol.Saboteador: return 2;
                default:             return 3;
            }
        }

        private static float _proximaRedifusion = 20f;

        /// <summary>
        /// El master repite el reparto cada 20 segundos.
        ///
        /// No es paranoia: quien entra a la partida con el nivel ya empezado se
        /// perdio el evento del reparto y se quedaria con la tabla vacia. Para el
        /// recien llegado eso significa que RolEnJuego(Ingeniero) da false y el
        /// pato le funcionaria, saltandose el rol. Repetirlo es un paquete
        /// minusculo (unos pocos strings) y cierra el agujero sin tener que
        /// engancharse a los eventos de conexion de Photon.
        /// </summary>
        private static void RedifundirDeVezEnCuando()
        {
            if (!_repartoHecho) return;
            if (!SemiFunc.IsMasterClient()) return;

            _proximaRedifusion -= Time.deltaTime;
            if (_proximaRedifusion > 0f) return;

            _proximaRedifusion = 20f;
            Difundir(false);
        }

        /// <summary>El master manda el reparto entero a los demas.</summary>
        /// <param name="repartoNuevo">
        /// Viaja en el paquete porque el cliente no puede deducirlo. Un sorteo de
        /// nivel nuevo tiene que reiniciarle las cargas aunque le vuelva a tocar el
        /// mismo rol; una reparacion o la redifusion periodica, no.
        /// </param>
        private static void Difundir(bool repartoNuevo)
        {
            if (!SemiFunc.IsMultiplayer()) return;
            if (PhotonNetwork.NetworkingClient == null) return;

            // Un unico string "N|steamid:rol;steamid:rol", en vez de un object[]
            // con un string[] y un byte[] dentro. Photon serializa arrays anidados,
            // pero es la parte de su serializador con mas esquinas raras y esto
            // corre en la maquina de otra persona: un string suelto es el tipo mas
            // basico que existe y no hay nada que pueda torcerse.
            // Los SteamID son numericos, asi que ni ':' ni ';' ni '|' aparecen.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(repartoNuevo ? 'N' : 'R').Append('|');

            bool primero = true;
            foreach (var par in _asignados)
            {
                if (!primero) sb.Append(';');
                primero = false;
                sb.Append(par.Key).Append(':').Append((byte)par.Value);
            }

            // A Others y no a All: el master ya se aplica el reparto por su cuenta
            // en Aplicar(), asi no depende de que el servidor le devuelva el eco.
            PhotonNetwork.RaiseEvent(
                EventoReparto,
                sb.ToString(),
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable);
        }

        private static void RecibirReparto(object datos)
        {
            string texto = datos as string;
            if (texto == null) return;

            // Marca de cabecera: 'N' sorteo nuevo, 'R' reparacion/redifusion.
            int barra = texto.IndexOf('|');
            if (barra < 0) return;                       // formato que no reconocemos

            bool   repartoNuevo = texto.Substring(0, barra) == "N";
            string cuerpo       = texto.Substring(barra + 1);

            Dictionary<string, Rol> recibido = new Dictionary<string, Rol>();

            if (cuerpo.Length > 0)
            {
                foreach (string trozo in cuerpo.Split(';'))
                {
                    int corte = trozo.LastIndexOf(':');
                    if (corte <= 0 || corte == trozo.Length - 1) continue;

                    byte valor;
                    if (!byte.TryParse(trozo.Substring(corte + 1), out valor)) continue;

                    recibido[trozo.Substring(0, corte)] = (Rol)valor;
                }
            }

            // El master repite el reparto cada 20 s por los que entran tarde, asi
            // que la mayoria de estos paquetes traen exactamente lo mismo que ya
            // teniamos. Hay que detectarlo ANTES de aplicar: Aplicar() dispara
            // AlRepartir, y eso le devolveria al medico sus 3 cargas y le quitaria
            // la recarga al saboteador cada 20 segundos.
            if (EsElMismoReparto(recibido)) return;

            _asignados.Clear();
            foreach (var par in recibido) _asignados[par.Key] = par.Value;

            _repartoHecho = true;
            Aplicar(repartoNuevo);
        }

        private static bool EsElMismoReparto(Dictionary<string, Rol> otro)
        {
            if (!_repartoHecho) return false;
            if (_asignados.Count != otro.Count) return false;

            foreach (var par in otro)
            {
                Rol tenia;
                if (!_asignados.TryGetValue(par.Key, out tenia)) return false;
                if (tenia != par.Value) return false;
            }

            return true;
        }
    }
}
