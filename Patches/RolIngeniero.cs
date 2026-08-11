using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using System.Reflection;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// ROL: INGENIERO.
    ///
    /// Recarga a tope la bateria del objeto que lleve en las manos. Vale para
    /// cualquier cosa con ItemBattery: armas, drones, rastreadores y lo que
    /// Semiwork anada en el futuro, sin tener que tocar el mod. Cargas limitadas
    /// que se reponen solas, mismo modelo que el medico.
    ///
    /// ---------------------------------------------------------------------
    /// LO QUE ESTE ROL ERA ANTES, Y POR QUE SE CAMBIO
    /// ---------------------------------------------------------------------
    /// Hasta la 1.4.5 el ingeniero era el unico que podia activar la extraccion.
    /// Sonaba bien y era un fallo de diseno: convertia a una persona en el cuello
    /// de botella del grupo entero. Si moria, se desconectaba, se quedaba lejos o
    /// no se habia enterado de que le habia tocado, el nivel no tenia salida.
    /// Quedo en un log: 88 rechazos seguidos, unos 44 segundos machacando el pato,
    /// y la partida murio ahi. Ahora extrae cualquiera y el ingeniero aporta algo
    /// que ayuda sin bloquear a nadie.
    ///
    /// ---------------------------------------------------------------------
    /// COMO SE RECARGA UNA BATERIA, Y LAS DOS TRAMPAS QUE TIENE
    /// ---------------------------------------------------------------------
    /// `ItemBattery.SetBatteryLife(int porcentaje)` es publico y hace justo lo que
    /// hace falta, pero leyendo su IL aparecen dos pegas que no se ven desde fuera:
    ///
    /// TRAMPA 1 — no resucita una bateria agotada. El metodo empieza asi:
    ///
    ///     if (batteryLife &gt; 0) { ...aplica el porcentaje... }
    ///     else                 { batteryLife = 0; batteryLifeInt = 0; }
    ///
    /// O sea que con la bateria a CERO —justo el caso que este rol existe para
    /// arreglar— la deja a cero y no hace nada. Por eso antes de llamarlo hay que
    /// meterle un valor positivo al campo `batteryLife`, que por suerte es publico.
    ///
    /// TRAMPA 2 — solo el anfitrion puede propagarlo. Al final SetBatteryLife llama
    /// a BatteryFullPercentChange, que es:
    ///
    ///     if (gameMode == 0)        LogicaLocal(...);            // un jugador
    ///     else if (IsMasterClient)  photonView.RPC(..., All);
    ///     else                      return;                      // &lt;-- cliente: nada
    ///
    /// Si el ingeniero es un cliente y llama a esto, no pasa absolutamente nada: ni
    /// en su maquina ni en las de los demas, y sin un solo error. Por eso el cliente
    /// manda `Roles.EventoRecarga` al anfitrion y es el anfitrion quien recarga.
    /// Es el mismo reparto de autoridad que ya usan la extraccion (evento 174) y el
    /// boost de enemigos del saboteador (176).
    /// </summary>
    internal static class RolIngeniero
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>    Enabled;
        internal static ConfigEntry<KeyCode> Tecla;
        internal static ConfigEntry<int>     Cargas;
        internal static ConfigEntry<float>   MinutosRecarga;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Ingeniero", "Activado", true,
                "Mete el rol de ingeniero en el sorteo. El ingeniero recarga la bateria " +
                "del objeto que lleve en las manos.");

            Tecla = config.Bind(
                "Ingeniero", "Tecla", KeyCode.J,
                "Tecla que recarga a tope el objeto que tengas agarrado.");

            Cargas = config.Bind(
                "Ingeniero", "Cargas", 5,
                new ConfigDescription("Reparaciones disponibles a la vez.",
                    new AcceptableValueRange<int>(1, 20)));

            MinutosRecarga = config.Bind(
                "Ingeniero", "MinutosRecarga", 5f,
                new ConfigDescription(
                    "Cada cuantos minutos se recupera UNA reparacion.",
                    new AcceptableValueRange<float>(0.1f, 30f)));

            // Igual que el medico: la suscripcion va aqui porque BindConfig se llama
            // una sola vez desde Plugin.Awake, y asi no depende de cuando el runtime
            // decida inicializar la clase.
            Roles.AlRepartir += () => { if (Roles.SoyEl(Rol.Ingeniero)) Reiniciar(); };
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
                $"[{Tecla.Value}] recargar lo que lleves en las manos  -  {_cargas}/{Cargas.Value} reparaciones",
                new Color(0.4f, 0.8f, 1f), Color.white, 5f);
        }

        // =====================================================================
        // Campos internos del juego (internal -> hay que ir por reflexion)
        // =====================================================================

        /// <summary>
        /// El objeto que el jugador tiene agarrado. Es `internal` en PhysGrabber,
        /// asi que no se puede tocar desde aqui sin reflexion.
        /// </summary>
        private static readonly FieldInfo _campoAgarrado =
            AccessTools.Field(typeof(PhysGrabber), "grabbedPhysGrabObject");

        private static PhysGrabObject LoQueLlevoEnLasManos()
        {
            PhysGrabber garra = PhysGrabber.instance;
            if (garra == null || !garra.grabbed) return null;
            if (_campoAgarrado == null) return null;

            return _campoAgarrado.GetValue(garra) as PhysGrabObject;
        }

        // =====================================================================
        // Tic
        // =====================================================================

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value) return;
            if (!Roles.SoyEl(Rol.Ingeniero)) return;
            if (!Roles.EnPartida()) return;

            RecargarCargas();

            if (Roles.TeclaPulsada(Tecla.Value)) Reparar();
        }

        private static void RecargarCargas()
        {
            if (_cargas >= Cargas.Value) return;

            _temporizador -= Time.deltaTime;
            if (_temporizador > 0f) return;

            _cargas++;
            _temporizador = MinutosRecarga.Value * 60f;

            SemiFunc.UIFocusText($"Reparacion recuperada  ({_cargas}/{Cargas.Value})",
                new Color(0.4f, 0.8f, 1f), Color.white, 3f);

            // Mismo motivo que en el medico: sin esta traza, la recarga seria el
            // unico camino del rol que no deja rastro en el Player.log, y no habria
            // forma de distinguir "el temporizador no corre" de "corre y el aviso
            // no se ve".
            Plugin.Debug($"Ingeniero: reparacion recuperada, ahora {_cargas}/{Cargas.Value}. " +
                         $"Siguiente en {MinutosRecarga.Value * 60f:0} s.");
        }

        // =====================================================================
        // Reparar
        // =====================================================================

        private static void Reparar()
        {
            Color azul   = new Color(0.4f, 0.8f, 1f);
            Color naranja = new Color(1f, 0.6f, 0.2f);

            if (_cargas <= 0)
            {
                int faltan = Mathf.CeilToInt(_temporizador);
                SemiFunc.UIFocusText($"Sin reparaciones  -  {faltan / 60}:{(faltan % 60):00} para la siguiente",
                    naranja, Color.white, 3f);
                return;
            }

            PhysGrabObject objeto = LoQueLlevoEnLasManos();
            if (objeto == null)
            {
                SemiFunc.UIFocusText("Agarra algo para repararlo", naranja, Color.white, 2f);
                return;
            }

            // La bateria no siempre cuelga del objeto raiz, asi que se busca tambien
            // en los hijos, e incluyendo los desactivados: un arma sin bateria puede
            // tener su indicador apagado.
            ItemBattery bateria = objeto.GetComponentInChildren<ItemBattery>(true);
            if (bateria == null)
            {
                SemiFunc.UIFocusText("Esto no lleva bateria", naranja, Color.white, 2f);
                return;
            }

            // El juego marca asi lo que no se puede cargar ni en la estacion.
            if (bateria.isUnchargable)
            {
                SemiFunc.UIFocusText("Esto no se puede recargar", naranja, Color.white, 2f);
                return;
            }

            // Ya lleno: no se gasta carga. Que una reparacion se evapore por pulsar
            // sobre algo que ya estaba a tope seria de las cosas mas molestas posibles.
            if (bateria.batteryLife >= 99.5f)
            {
                SemiFunc.UIFocusText("Ya esta a tope de bateria", naranja, Color.white, 2f);
                return;
            }

            // Hasta aqui no se ha gastado nada: todos los rechazos de arriba salen
            // sin coste.
            _cargas--;
            if (_cargas == Cargas.Value - 1)
                _temporizador = MinutosRecarga.Value * 60f;

            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                RecargarDelTodo(bateria);
            }
            else
            {
                // TRAMPA 2 de la cabecera: un cliente no puede propagar el cambio, asi
                // que se lo pide al anfitrion. Se manda el ViewID del objeto porque es
                // lo unico que las dos maquinas saben identificar igual.
                PhotonView vista = objeto.GetComponent<PhotonView>();
                if (vista == null)
                {
                    SemiFunc.UIFocusText("No puedo reparar esto por red", naranja, Color.white, 2f);
                    Plugin.Log.LogWarning("Ingeniero: el objeto no tiene PhotonView; no se puede pedir la recarga.");
                    _cargas++;   // no se cobra lo que no se ha hecho
                    return;
                }

                PhotonNetwork.RaiseEvent(
                    Roles.EventoRecarga,
                    vista.ViewID,
                    new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
                    SendOptions.SendReliable);

                Plugin.Debug($"Ingeniero: no soy master, recarga pedida para el objeto {vista.ViewID}.");
            }

            SemiFunc.UIFocusText($"Bateria al maximo  ({_cargas}/{Cargas.Value})",
                azul, Color.white, 3f);

            Plugin.Debug($"Ingeniero: '{objeto.name}' recargado. Quedan {_cargas} reparaciones.");
        }

        /// <summary>
        /// Deja la bateria al 100%. Solo tiene efecto en el anfitrion o en un jugador
        /// (ver TRAMPA 2 de la cabecera).
        /// </summary>
        private static void RecargarDelTodo(ItemBattery bateria)
        {
            // TRAMPA 1 de la cabecera: SetBatteryLife no levanta una bateria agotada,
            // asi que primero se le da un empujon por encima de cero. El valor da
            // igual, solo tiene que ser positivo para que entre por la rama buena.
            if (bateria.batteryLife <= 0f) bateria.batteryLife = 1f;

            // El parametro es un PORCENTAJE, no un numero de barras: el metodo hace
            // batteryLifeInt = round(batteryLife / (100 / batteryBars)).
            bateria.SetBatteryLife(100);
        }

        /// <summary>
        /// Lo llama Roles.AlRecibirEvento cuando un ingeniero cliente pide una
        /// recarga. Solo corre en el anfitrion.
        /// </summary>
        internal static void EjecutarRecargaRemota(object datos)
        {
            if (!(datos is int)) return;

            PhotonView vista = PhotonView.Find((int)datos);
            if (vista == null) return;

            ItemBattery bateria = vista.GetComponentInChildren<ItemBattery>(true);
            if (bateria == null || bateria.isUnchargable) return;

            RecargarDelTodo(bateria);

            Plugin.Debug($"Ingeniero: recarga remota aplicada al objeto {(int)datos}.");
        }
    }
}
