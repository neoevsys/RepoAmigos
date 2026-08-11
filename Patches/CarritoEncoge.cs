using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// OBJETOS MAS PEQUENOS DENTRO DEL C.A.R.T.
    ///
    /// Lo que esta dentro del carrito se encoge, y al salir recupera su tamano.
    /// Asi caben mas cosas y se ve mejor lo que llevas.
    ///
    /// =====================================================================
    /// POR QUE ESTO SE REESCRIBIO ENTERO EN LA 1.5.0
    /// =====================================================================
    /// La version anterior se enganchaba a dos sitios y los dos estaban mal:
    ///
    ///   1. `PhysGrabInCart.Add` — el juego SOLO lo llama en el anfitrion, asi que
    ///      los clientes no veian encogerse nada.
    ///   2. `PhysGrabObjectImpactDetector.OnTriggerStay` — se puso para tapar lo
    ///      anterior en los clientes, y no podia funcionar: en una maquina que no es
    ///      el master TODO PhysGrabObject se queda cinematico de por vida.
    ///      `PhysGrabObject.Awake` pone `rb.isKinematic = true` y lo unico que lo
    ///      deshace es la corrutina `EnableRigidbody()`, que solo arranca si
    ///      `!Multiplayer || IsMasterClient`. Unity no entrega OnTriggerStay a
    ///      cuerpos dormidos, asi que ahi moria el efecto. (El campo publico
    ///      `clientNonKinematic` parece la via de escape, pero lo escriben cuatro
    ///      clases y NO LO LEE NADIE: esta muerto.)
    ///
    /// Ademas hacia dano de verdad, y esto salio de los logs, no de la teoria: no
    /// filtraba QUE encogia. Se encogieron puertas del nivel (`Wizard Door Double`,
    /// que nunca se restauraron), bisagras, cabezas de muerto, el ragdoll de un
    /// jugador, una mina armada y el propio pato de ExtractionOnDemand — o sea dos
    /// funciones de este mod peleandose por el mismo transform. En una sola sesion:
    /// 68 encogidos, 29 restaurados, 39 objetos pequenos para siempre.
    ///
    /// Y habia una bomba sin estallar: `ItemEquippable` usa la escala como VARIABLE
    /// DE ESTADO (`AnimateEquip` la baja a originalScale*0.01 y `StateEquipped`
    /// comprueba `magnitude &lt; 0.1f`). Escribirle la escala a un arma justo mientras
    /// se equipa puede dejarla rota de forma permanente. Por eso ahora los
    /// ItemEquippable estan excluidos por norma.
    ///
    /// =====================================================================
    /// COMO FUNCIONA AHORA
    /// =====================================================================
    /// Un unico enganche: un Postfix sobre `PhysGrabCart.ObjectsInCart`.
    ///
    /// Ese metodo es la forma en que el propio juego decide que hay en el carrito, y
    /// es justo lo que hacia falta:
    ///
    ///   - `PhysGrabCart.Update` lo llama SIN puerta de master (IL_0066), o sea que
    ///     corre en todas las maquinas.
    ///   - Por dentro usa `Physics.OverlapBox`, que es una consulta de escena e
    ///     IGNORA que los rigidbodies esten dormidos. Los propios desarrolladores
    ///     decidieron no fiarse de los triggers para esto.
    ///   - Se autolimita: baja `objectInCartCheckTimer` con deltaTime y solo barre
    ///     cuando llega a cero, recargandolo a 0.5 s.
    ///
    /// O sea, dos reconciliaciones por segundo y carrito, en vez de miles de
    /// callbacks de fisica por segundo. Y funciona igual en anfitrion y en clientes.
    /// </summary>
    internal static class CarritoEncoge
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>  Enabled;
        internal static ConfigEntry<float> Escala;
        internal static ConfigEntry<bool>  SoloValiosos;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Carrito", "Activado", true,
                "Encoge los objetos mientras estan dentro del C.A.R.T. y les devuelve " +
                "su tamano al sacarlos.");

            Escala = config.Bind(
                "Carrito", "EscalaDentro", 0.75f,
                new ConfigDescription(
                    "Tamano dentro del carrito respecto al original. 0.75 los deja al 75%; " +
                    "0.25 seria encogerlos un 75%.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            SoloValiosos = config.Bind(
                "Carrito", "SoloValiosos", true,
                "true: solo se encoge el botin, que es lo que de verdad ocupa sitio. " +
                "false: tambien objetos normales. Aun en false hay cosas que NUNCA se " +
                "tocan (armas equipables, jugadores, enemigos, puertas y bisagras), " +
                "porque encogerlas rompia el nivel o el propio objeto.");
        }

        // =====================================================================
        // Registro de lo encogido
        // =====================================================================

        private sealed class Encogido
        {
            public Vector3 TamanoOriginal;
            public float   VistoPorUltimaVez;
        }

        private static readonly Dictionary<PhysGrabObject, Encogido> _dentro =
            new Dictionary<PhysGrabObject, Encogido>();

        /// <summary>
        /// Margen antes de dar por salido un objeto. El barrido del juego es cada
        /// 0.5 s, asi que 1.6 s son tres barridos: aguanta que se pierda alguno sin
        /// ponerse a parpadear encoger/restaurar, que es lo que hacia la version
        /// vieja (en un log, el mismo objeto encogido tres veces seguidas).
        /// </summary>
        private const float MargenSalida = 1.6f;

        // =====================================================================
        // Que se puede encoger y que no
        // =====================================================================

        // Estas banderas son `internal` en PhysGrabObject, asi que hay que ir por
        // reflexion.
        //
        // Van con el nombre ESCRITO A MANO, uno por linea, y no por un metodo que
        // reciba el nombre como parametro: verificar.ps1 busca literalmente
        // `AccessTools.Field(typeof(X), "campo")` en el IL, asi que solo puede
        // comprobar que el campo existe si el nombre es una constante. Con un
        // parametro compila igual pero se pierde la unica red que avisa de que un
        // parche del juego ha renombrado algo, y nos enterariamos jugando.
        private static readonly FieldInfo _campoValioso =
            AccessTools.Field(typeof(PhysGrabObject), "isValuable");
        private static readonly FieldInfo _campoEnemigo =
            AccessTools.Field(typeof(PhysGrabObject), "isEnemy");
        private static readonly FieldInfo _campoJugador =
            AccessTools.Field(typeof(PhysGrabObject), "isPlayer");

        /// <summary>
        /// Lee una bandera del juego. Si el campo ya no existe (parche del juego),
        /// devuelve `siNoSePuede`, que en cada llamada se elige para que la duda
        /// juegue a favor de NO tocar el objeto.
        /// </summary>
        private static bool LaBanderaDice(FieldInfo campo, PhysGrabObject objeto, bool siNoSePuede)
        {
            if (campo == null) return siNoSePuede;
            object valor = campo.GetValue(objeto);
            return valor is bool ? (bool)valor : siNoSePuede;
        }

        private static bool SePuedeEncoger(PhysGrabObject objeto)
        {
            if (objeto == null) return false;

            // Nunca: otros carritos, enemigos y jugadores. Ante la duda (campo que ya
            // no existe), se asume que SI lo es y se deja en paz.
            if (objeto.GetComponent<PhysGrabCart>() != null) return false;
            if (LaBanderaDice(_campoEnemigo, objeto, true)) return false;
            if (LaBanderaDice(_campoJugador, objeto, true)) return false;

            // Nunca: nada equipable. La escala es su variable de estado interna y
            // escribirsela en el momento justo deja el arma rota para siempre.
            if (objeto.GetComponentInChildren<ItemEquippable>(true) != null) return false;

            // Nunca: cabezas de muerto ni bisagras. Las cabezas son un jugador
            // esperando a revivir, y las bisagras son puertas del nivel — se
            // quedaron encogidas para siempre en varias partidas.
            if (objeto.GetComponentInChildren<PlayerDeathHead>(true) != null) return false;
            if (objeto.GetComponentInChildren<PhysGrabHinge>(true) != null) return false;

            // Por defecto, solo el botin. Ante la duda, NO es valioso y no se toca.
            if (SoloValiosos.Value && !LaBanderaDice(_campoValioso, objeto, false)) return false;

            return true;
        }

        // =====================================================================
        // El motor: un Postfix sobre el barrido del propio juego
        // =====================================================================

        [HarmonyPatch(typeof(PhysGrabCart), "ObjectsInCart")]
        private static class ReconciliarContenido
        {
            /// <summary>
            /// Guarda el temporizador ANTES. `ObjectsInCart` se llama cada frame pero
            /// solo barre de verdad cuando el temporizador llega a cero, y entonces lo
            /// recarga a 0.5. Comparando antes/despues sabemos si la lista se acaba de
            /// refrescar; si no, no hay nada nuevo que mirar y nos ahorramos el trabajo.
            /// </summary>
            private static void Prefix(out float __state, float ___objectInCartCheckTimer)
            {
                __state = ___objectInCartCheckTimer;
            }

            private static void Postfix(float __state, float ___objectInCartCheckTimer,
                                        List<PhysGrabObject> ___itemsInCart)
            {
                if (Enabled == null || !Enabled.Value) return;

                // El temporizador solo SUBE cuando se acaba de hacer el barrido.
                if (___objectInCartCheckTimer <= __state) return;

                if (___itemsInCart != null)
                    foreach (PhysGrabObject objeto in ___itemsInCart) Marcar(objeto);

                DevolverALosQueSalieron();
            }
        }

        private static void Marcar(PhysGrabObject objeto)
        {
            if (objeto == null) return;

            Encogido registro;
            if (_dentro.TryGetValue(objeto, out registro))
            {
                registro.VistoPorUltimaVez = Time.time;   // sigue dentro
                return;
            }

            if (!SePuedeEncoger(objeto)) return;

            Vector3 original = objeto.transform.localScale;

            // Un objeto a mitad de una animacion de equipar/soltar tiene la escala
            // casi a cero. Guardar ESO como "tamano original" lo dejaria diminuto
            // para siempre al restaurarlo.
            if (original.sqrMagnitude < 0.01f) return;

            _dentro[objeto] = new Encogido
            {
                TamanoOriginal    = original,
                VistoPorUltimaVez = Time.time
            };

            objeto.transform.localScale = original * Escala.Value;

            Plugin.Debug($"Carrito: '{objeto.name}' encogido al {Escala.Value:P0}.");
        }

        private static void DevolverALosQueSalieron()
        {
            if (_dentro.Count == 0) return;

            List<PhysGrabObject> fuera = null;

            foreach (var par in _dentro)
            {
                // Objeto destruido (extraido, roto...): solo hay que olvidarlo.
                if (par.Key == null)
                {
                    (fuera ?? (fuera = new List<PhysGrabObject>())).Add(par.Key);
                    continue;
                }

                if (Time.time - par.Value.VistoPorUltimaVez < MargenSalida) continue;

                par.Key.transform.localScale = par.Value.TamanoOriginal;
                (fuera ?? (fuera = new List<PhysGrabObject>())).Add(par.Key);

                Plugin.Debug($"Carrito: '{par.Key.name}' recupera su tamano.");
            }

            if (fuera != null)
                foreach (PhysGrabObject p in fuera) _dentro.Remove(p);
        }

        // =====================================================================
        // Red de seguridad al cambiar de nivel
        // =====================================================================

        /// <summary>
        /// Al cambiar de nivel se restaura TODO y se vacia el registro.
        ///
        /// Sin esto quedaban objetos encogidos para siempre: si el carrito
        /// desaparece o el nivel se descarga, `ObjectsInCart` deja de llamarse y
        /// nadie devuelve el tamano. En una sola sesion se quedaron 39 asi.
        ///
        /// Restaurar ANTES de olvidar es idempotente: si el objeto sigue dentro del
        /// carrito, el siguiente barrido lo vuelve a registrar con su tamano bueno.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
        private static class SoltarTodoAlCambiarDeNivel
        {
            private static void Prefix()
            {
                if (_dentro.Count == 0) return;

                int devueltos = 0;
                foreach (var par in _dentro)
                {
                    if (par.Key == null) continue;
                    par.Key.transform.localScale = par.Value.TamanoOriginal;
                    devueltos++;
                }

                Plugin.Debug($"Carrito: cambio de nivel, {devueltos} objetos devueltos a su tamano.");
                _dentro.Clear();
            }
        }
    }
}
