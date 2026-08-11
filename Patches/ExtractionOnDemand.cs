using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// EXTRACCION BAJO DEMANDA.
    ///
    /// En vanilla, al final de ExtractionPoint.HaulChecker:
    ///
    ///     if (haulGoal - haulCurrent &lt;= 0 &amp;&amp; haulGoalFetched) {
    ///         successDelay -= Time.deltaTime;
    ///         if (successDelay &lt;= 0f) { StateSet(State.Success); return; }
    ///     }
    ///
    /// O sea: en cuanto cubres la meta, 1.5 s despues la extraccion se dispara sola.
    ///
    /// Este parche la bloquea y deja el extractor "listo y esperando". La extraccion
    /// solo arranca cuando un jugador va y pulsa el boton del payaso. Asi el grupo
    /// decide cuando cerrar, y da tiempo a meter mas cosas para el excedente (surplus).
    ///
    /// Autoridad: StateSet solo tiene efecto en el master client y se propaga por RPC
    /// (StateSetRPC). Si quien pulsa NO es el master, se manda un evento Photon al
    /// master para que sea el quien ejecute. Todos los jugadores necesitan el mod.
    /// </summary>
    internal static class ExtractionOnDemand
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>   Enabled;
        internal static ConfigEntry<bool>   AvisoEnPantalla;
        internal static ConfigEntry<string> TextoPantalla;
        internal static ConfigEntry<bool>   BotonGrande;
        internal static ConfigEntry<float>  DesplazamientoX;
        internal static ConfigEntry<float>  DesplazamientoY;
        internal static ConfigEntry<float>  DesplazamientoZ;
        internal static ConfigEntry<bool>   SoloElBoton;
        internal static ConfigEntry<float>  EscalaBoton;
        internal static ConfigEntry<string> PiezasVisibles;
        internal static ConfigEntry<float>  GiroBoton;
        internal static ConfigEntry<bool>   UsarPato;
        internal static ConfigEntry<float>  EscalaPato;
        internal static ConfigEntry<string> NombrePato;
        internal static ConfigEntry<float>  GiroPatoX;
        internal static ConfigEntry<float>  GiroPatoY;
        internal static ConfigEntry<float>  GiroPatoZ;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "ExtraccionManual", "Activado", true,
                "Si esta activado, al cubrir la meta el extractor NO se dispara solo: " +
                "espera a que alguien pulse el boton. Ponlo en false para volver al comportamiento original.");

            AvisoEnPantalla = config.Bind(
                "ExtraccionManual", "AvisoEnPantalla", true,
                "Cambia el texto del tubo del extractor cuando ya esta listo, para que se vea que falta pulsarlo.");

            TextoPantalla = config.Bind(
                "ExtraccionManual", "TextoAviso", "PULSA EL BOTON",
                "Texto que aparece en el extractor cuando la meta ya esta cubierta.");

            BotonGrande = config.Bind(
                "ExtraccionManual", "BotonGrandeDeTienda", true,
                "Conserva el pedestal con el boton grande que los extractores solo tienen en la tienda, " +
                "y lo hace aparecer cuando la meta esta cubierta. En vanilla el juego lo destruye al " +
                "arrancar cualquier nivel que no sea la tienda.");

            // El pedestal se coloca en la rampa de entrada del extractor. Estos tres
            // valores lo mueven desde ahi, para afinar sin recompilar nada.
            DesplazamientoX = config.Bind("ExtraccionManual", "BotonLateral", 2.5f,
                new ConfigDescription("Mueve el boton grande a un lado u otro de la rampa, en metros.",
                    new AcceptableValueRange<float>(-20f, 20f)));

            DesplazamientoY = config.Bind("ExtraccionManual", "BotonAltura", 1.1f,
                new ConfigDescription("Altura del boton sobre el suelo de la rampa, en metros.",
                    new AcceptableValueRange<float>(-20f, 20f)));

            UsarPato = config.Bind("ExtraccionManual", "UsarPato", true,
                "En vez del boton de la tienda, pone un pato de goma fijo al que agarrar para lanzar " +
                "la extraccion. Mucho mas visible y no hay que pelearse con la geometria del mostrador.");

            NombrePato = config.Bind("ExtraccionManual", "PatoNombreDelItem", "Rubber Duck",
                "Nombre exacto del item que se usa como boton. Si no lo encuentra, coge el primero " +
                "que lleve 'duck' en el nombre. El log lista todos los disponibles.");

            // Sobre la orientacion natural del item (spawnRotationOffset).
            GiroPatoX = config.Bind("ExtraccionManual", "PatoGiroX", 0f,
                new ConfigDescription("Inclina el pato hacia delante o atras, en grados.",
                    new AcceptableValueRange<float>(-360f, 360f)));

            GiroPatoY = config.Bind("ExtraccionManual", "PatoGiroY", 0f,
                new ConfigDescription("Gira el pato sobre si mismo para que mire a donde quieras, en grados.",
                    new AcceptableValueRange<float>(-360f, 360f)));

            GiroPatoZ = config.Bind("ExtraccionManual", "PatoGiroZ", 0f,
                new ConfigDescription("Ladea el pato, en grados.",
                    new AcceptableValueRange<float>(-360f, 360f)));

            EscalaPato = config.Bind("ExtraccionManual", "PatoEscala", 2.0f,
                new ConfigDescription("Tamano del pato. 1 es su tamano normal.",
                    new AcceptableValueRange<float>(0.2f, 10f)));

            GiroBoton = config.Bind("ExtraccionManual", "BotonGiro", 0f,
                new ConfigDescription("Gira el boton sobre si mismo, en grados. Prueba 180 si te da la espalda.",
                    new AcceptableValueRange<float>(-360f, 360f)));

            DesplazamientoZ = config.Bind("ExtraccionManual", "BotonHaciaFuera", 1.0f,
                new ConfigDescription("Aleja el boton grande del extractor (positivo) o lo acerca (negativo), en metros.",
                    new AcceptableValueRange<float>(-20f, 20f)));

            SoloElBoton = config.Bind("ExtraccionManual", "SoloElBoton", true,
                "El pedestal de la tienda incluye el mostrador entero. Con esto activado solo se " +
                "muestran las piezas indicadas en PiezasDelBoton y se oculta el resto.");

            PiezasVisibles = config.Bind("ExtraccionManual", "PiezasDelBoton",
                "Extraction Point Side Button, Shop Button",
                "Piezas del pedestal que se dejan visibles, separadas por comas. Disponibles: " +
                "'Shop Button' (la placa roja, el boton en si), 'Extraction Point Side Button' " +
                "(la consola sobre la que va montada la placa), 'Meshtownusa' y 'Cube (1)' " +
                "(el mostrador de la tienda, un armatoste).");

            EscalaBoton = config.Bind("ExtraccionManual", "BotonEscala", 1.0f,
                new ConfigDescription("Tamano del boton. 0.5 lo deja a la mitad, 2 al doble.",
                    new AcceptableValueRange<float>(0.1f, 5f)));
        }

        // =====================================================================
        // Estado por extractor
        //
        // ConditionalWeakTable en vez de un HashSet: cuando Unity destruye el
        // extractor al cambiar de nivel, la entrada se recolecta sola. Con un
        // HashSet iriamos acumulando referencias muertas partida tras partida.
        // =====================================================================

        private sealed class Estado
        {
            /// <summary>Meta cubierta y esperando a que alguien pulse el boton.</summary>
            public bool Esperando;

            /// <summary>El boton ya se pulso: el proximo StateSet(Success) puede pasar.</summary>
            public bool Armado;

            /// <summary>Para no repintar la pantalla del tubo en cada frame.</summary>
            public bool AvisoMostrado;

            /// <summary>Intensidad y color originales de la luz del boton, para devolverlos luego.</summary>
            public float LuzIntensidad = -1f;
            public Color LuzColor;

            /// <summary>
            /// El pedestal con el boton grande, rescatado del Destroy de Start.
            /// Lo guardamos aqui porque el campo shopStation del juego lo dejamos
            /// a null a proposito (ver ConservarBotonDeTienda).
            /// </summary>
            public Transform Estacion;

            /// <summary>Este extractor ya completo su extraccion: el boton sobra.</summary>
            public bool YaCompletado;

            /// <summary>El pato de goma que hace de boton, si esta activada esa opcion.</summary>
            public PhysGrabObject Pato;

            /// <summary>Sitio exacto donde debe estar el pato, para devolverlo si lo empujan.</summary>
            public Vector3    PatoPosicion;
            public Quaternion PatoRotacion;

            // Por que NO se puede extraer todavia. Esperando resume las tres
            // condiciones en un bool, y con eso solo no se puede decirle al jugador
            // que le falta: se guardan por separado para el aviso de pantalla.
            /// <summary>El extractor esta en marcha (State.Active).</summary>
            public bool Activo;
            /// <summary>El juego ya sabe cuanto botin pide este extractor.</summary>
            public bool MetaConocida;
            /// <summary>Botin que falta para cubrir la meta. 0 o menos = cubierta.</summary>
            public int  FaltaBotin;
        }

        private static readonly ConditionalWeakTable<ExtractionPoint, Estado> _estados =
            new ConditionalWeakTable<ExtractionPoint, Estado>();

        private static Estado EstadoDe(ExtractionPoint punto) =>
            _estados.GetOrCreateValue(punto);

        // =====================================================================
        // Red: evento Photon propio
        //
        // No se puede anadir un [PunRPC] a una clase del juego desde fuera, asi que
        // usamos RaiseEvent con un codigo propio. El rango 1..199 es libre para
        // usuarios; PUN se reserva del 200 en adelante.
        // =====================================================================

        private const byte EventoPulsarBoton = 174;

        private static bool _redConectada;

        private static void AsegurarRed()
        {
            if (_redConectada) return;
            if (PhotonNetwork.NetworkingClient == null) return;   // PUN aun no esta listo

            PhotonNetwork.NetworkingClient.EventReceived += AlRecibirEvento;
            _redConectada = true;
            Plugin.Debug("Enganchado al canal de eventos de Photon.");
        }

        internal static void DesconectarRed()
        {
            if (!_redConectada) return;
            if (PhotonNetwork.NetworkingClient != null)
                PhotonNetwork.NetworkingClient.EventReceived -= AlRecibirEvento;
            _redConectada = false;
        }

        private static void AlRecibirEvento(EventData datos)
        {
            if (datos.Code != EventoPulsarBoton) return;
            if (!SemiFunc.IsMasterClient()) return;          // solo el master ejecuta

            try
            {
                int viewId = (int)datos.CustomData;
                PhotonView view = PhotonView.Find(viewId);
                if (view == null) return;

                ExtractionPoint punto = view.GetComponent<ExtractionPoint>();
                if (punto == null) return;

                // Solo si de verdad esta esperando. Evita que un cliente
                // desincronizado fuerce una extraccion que no toca.
                if (!EstadoDe(punto).Esperando) return;

                Plugin.Debug($"Peticion de extraccion recibida para el extractor {viewId}.");
                Ejecutar(punto);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error procesando el evento de extraccion: {e}");
            }
        }

        // =====================================================================
        // Disparo de la extraccion
        // =====================================================================

        /// <summary>Pulsacion del boton: la ejecuta el master, o se la pide.</summary>
        private static void Solicitar(ExtractionPoint punto, PhotonView view)
        {
            // AQUI ESTABA EL CANDADO DEL INGENIERO, y se quito a proposito en la
            // 1.5.0: la extraccion vuelve a ser de todos.
            //
            // La idea era que solo un jugador pudiera lanzarla. En la practica
            // convertia a esa persona en el cuello de botella del grupo entero: si
            // moria, se desconectaba, se quedaba lejos o simplemente no se habia
            // enterado de que era el ingeniero, el nivel se quedaba sin salida y
            // los demas solo veian un mensaje que ni siquiera decia quien era.
            //
            // Paso de verdad y quedo en el log: 88 rechazos seguidos, unos 44
            // segundos machacando el pato, y la partida murio ahi.
            //
            // Si algun dia se vuelve a poner un filtro de quien puede extraer, este
            // es el sitio: las TRES rutas de pulsacion (el pato por GrabStarted, el
            // boton lateral por OnClick y el de la tienda por OnShopClick) pasan
            // todas por aqui, asi que es el unico punto donde no se puede esquivar.
            // Ese fue el error de la 1.3.x: filtraba solo OnClick y el pato — que es
            // el que se usa de verdad — se colaba.

            // Feedback local inmediato (animacion y sonido del boton) para quien pulsa,
            // sin esperar al ida y vuelta con el master.
            punto.ButtonPress();

            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                Ejecutar(punto);
                return;
            }

            if (view == null)
            {
                Plugin.Log.LogWarning("Extractor sin PhotonView: no se puede avisar al master.");
                return;
            }

            PhotonNetwork.RaiseEvent(
                EventoPulsarBoton,
                view.ViewID,
                new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
                SendOptions.SendReliable);

            Plugin.Debug($"No soy master: peticion enviada para el extractor {view.ViewID}.");
        }

        /// <summary>Arma el permiso y lanza el Success real. Solo master/singleplayer.</summary>
        private static void Ejecutar(ExtractionPoint punto)
        {
            Estado estado = EstadoDe(punto);
            estado.Armado = true;

            // StateSet es privado: hay que llamarlo por reflexion cacheada.
            _stateSet.Invoke(punto, new object[] { ExtractionPoint.State.Success });
        }

        private static readonly System.Reflection.MethodInfo _stateSet =
            AccessTools.Method(typeof(ExtractionPoint), "StateSet");

        private static readonly System.Reflection.MethodInfo _tubeScreenTextChange =
            AccessTools.Method(typeof(ExtractionPoint), "TubeScreenTextChange");

        private static readonly System.Reflection.FieldInfo _buttonOriginalMaterial =
            AccessTools.Field(typeof(ExtractionPoint), "buttonOriginalMaterial");

        /// <summary>
        /// Devuelve al boton su aspecto de "pulsable".
        ///
        /// ButtonToggle(false) hace tres cosas al activarse el extractor:
        ///   button.material = buttonOff;      // queda oscuro, no se ve
        ///   buttonGrabObject.enabled = false; // no se puede agarrar
        ///   buttonLight.enabled = false;      // sin luz
        ///
        /// Reactivar solo los dos ultimos deja el boton funcional pero invisible.
        /// Hay que restaurar tambien el material original.
        ///
        /// Se llama una sola vez por espera: asignar .material cada frame crearia
        /// una instancia nueva de material en cada uno.
        /// </summary>
        private static void EncenderBoton(ExtractionPoint punto, Estado estado)
        {
            try
            {
                var original = _buttonOriginalMaterial?.GetValue(punto) as Material;
                if (original != null && punto.button != null)
                    punto.button.material = original;

                // Luz verde y mas intensa: el boton esta lejos y a oscuras.
                if (punto.buttonLight != null && estado.LuzIntensidad < 0f)
                {
                    estado.LuzIntensidad = punto.buttonLight.intensity;
                    estado.LuzColor      = punto.buttonLight.color;

                    punto.buttonLight.intensity = Mathf.Max(estado.LuzIntensidad * 3f, 5f);
                    punto.buttonLight.color     = Color.green;
                }

                // Deja en el log donde esta el boton, para poder decirte hacia
                // donde mirar si aun asi no lo encuentras.
                if (punto.buttonGrabObject != null)
                {
                    Vector3 p = punto.buttonGrabObject.transform.position;
                    Vector3 e = punto.transform.position;
                    Plugin.Debug(
                        $"Boton en {p.ToString("F1")}, extractor en {e.ToString("F1")}, " +
                        $"separacion {Vector3.Distance(p, e):F1} m.");
                }

                // El pedestal ya esta puesto desde Start; aqui no hay que tocarlo.
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"No se pudo encender el boton: {e.Message}");
            }
        }

        // =====================================================================
        // PARCHE 0 — rescatar el pedestal con el boton grande
        //
        // ExtractionPoint.Start termina asi en cualquier nivel que no sea tienda:
        //
        //     isShop = SemiFunc.RunIsShop();
        //     if (!isShop) { Destroy(shopStation.gameObject); return; }
        //
        // O sea, el boton grande NO esta oculto: el juego lo destruye. Por eso no
        // bastaba con hacerle SetActive(true).
        //
        // El transpiler sustituye esa unica llamada a Destroy (es la unica de toda
        // la clase, comprobado) por un metodo nuestro que decide si destruir.
        //
        // Ojo con el efecto colateral: StateComplete hace
        //     if (shopStation != null) { ...se salta el refresco de iconos del mapa... }
        // Si dejamos el campo apuntando al pedestal, el juego trataria el nivel como
        // una tienda al completar la extraccion y los iconos del mapa se quedarian
        // sin actualizar. Por eso el Postfix guarda la referencia por nuestra cuenta
        // y deja el campo del juego a null: reutilizamos el objeto, no su funcion.
        // =====================================================================

        [HarmonyPatch(typeof(ExtractionPoint), "Start")]
        private static class ConservarBotonDeTienda
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrucciones)
            {
                var destroy = AccessTools.Method(
                    typeof(UnityEngine.Object), "Destroy", new[] { typeof(UnityEngine.Object) });
                var reemplazo = AccessTools.Method(
                    typeof(ExtractionOnDemand), nameof(DestruirSalvoQueLoConservemos));

                foreach (CodeInstruction ins in instrucciones)
                {
                    if (destroy != null && ins.Calls(destroy))
                        yield return new CodeInstruction(OpCodes.Call, reemplazo);
                    else
                        yield return ins;
                }
            }

            private static void Postfix(ExtractionPoint __instance, bool ___isShop)
            {
                if (!Enabled.Value || !BotonGrande.Value) return;
                if (___isShop) return;                       // en la tienda, todo normal

                Transform estacion = __instance.shopStation;
                if (estacion == null) return;                // no sobrevivio o no existe

                EstadoDe(__instance).Estacion = estacion;

                // Clave: el juego debe seguir creyendo que aqui no hay tienda.
                __instance.shopStation = null;

                // Visible desde el primer momento, no solo al cubrir la meta: asi
                // el grupo ve desde el principio donde hay que ir a pulsar.
                estacion.gameObject.SetActive(true);
                DesplegarEstacion(estacion, __instance);
            }
        }

        /// <summary>
        /// Saca el pedestal, lo coloca donde se pueda ver y alcanzar, y deja en el
        /// log todo lo necesario para afinar la posicion o para saber por que no se
        /// ve: donde acabo, si esta activo de verdad en la jerarquia, su escala y
        /// cuantos renderers tenia apagados.
        /// </summary>
        private static void DesplegarEstacion(Transform estacion, ExtractionPoint punto)
        {
            try
            {
                // Hijos y renderers pueden venir apagados por su cuenta.
                foreach (Transform hijo in estacion.GetComponentsInChildren<Transform>(true))
                    if (!hijo.gameObject.activeSelf) hijo.gameObject.SetActive(true);

                int reactivados = 0, total = 0;
                foreach (Renderer r in estacion.GetComponentsInChildren<Renderer>(true))
                {
                    total++;
                    if (!r.enabled) { r.enabled = true; reactivados++; }
                }

                // El "Shop Station" no es un boton: es el mostrador entero de la
                // tienda (hijos 'Meshtownusa', 'Cube (1)'...). Puesto en un nivel
                // normal es un armatoste que ademas tapa el extractor. Nos quedamos
                // solo con las piezas cuyo nombre lleva "Button".
                if (SoloElBoton.Value)
                {
                    var permitidas = new List<string>();
                    foreach (string s in (PiezasVisibles.Value ?? "").Split(','))
                        if (!string.IsNullOrEmpty(s.Trim())) permitidas.Add(s.Trim());

                    var ocultados = new List<string>();
                    foreach (Transform hijo in estacion)
                    {
                        bool visible = false;
                        foreach (string nombre in permitidas)
                            if (string.Equals(nombre, hijo.name, StringComparison.OrdinalIgnoreCase))
                            { visible = true; break; }

                        hijo.gameObject.SetActive(visible);
                        if (!visible) ocultados.Add(hijo.name);
                    }

                    // Alguna malla puede colgar del propio raiz, no de un hijo.
                    foreach (Renderer r in estacion.GetComponents<Renderer>())
                        r.enabled = false;

                    if (ocultados.Count > 0)
                        Plugin.Debug($"Mobiliario oculto: {string.Join(", ", ocultados.ToArray())}");
                }

                estacion.localScale = Vector3.one * EscalaBoton.Value;

                // Referencia: la rampa de entrada. Es por donde entran los jugadores,
                // asi que es el sitio natural para el boton.
                //
                // El intento anterior partia de la posicion horizontal del boton
                // pequeno, y salio mal: ese boton esta arriba en la estructura del
                // tubo, al fondo, asi que el pedestal acababa metido en la pared.
                Vector3 origen = punto.ramp != null      ? punto.ramp.position
                               : punto.platform != null  ? punto.platform.position
                               :                           punto.transform.position;

                // "Hacia fuera" = del extractor hacia la rampa, en horizontal.
                Vector3 haciaFuera = origen - punto.transform.position;
                haciaFuera.y = 0f;
                haciaFuera = haciaFuera.sqrMagnitude < 0.01f
                           ? punto.transform.forward
                           : haciaFuera.normalized;

                Vector3 lateral = Vector3.Cross(Vector3.up, haciaFuera);

                // La rotacion va PRIMERO: girar el pedestal mueve a sus hijos
                // alrededor de su raiz, asi que el desfase hay que medirlo despues.
                estacion.rotation = Quaternion.LookRotation(haciaFuera, Vector3.up)
                                  * Quaternion.Euler(0f, GiroBoton.Value, 0f);

                Vector3 destino = origen
                                + lateral    * DesplazamientoX.Value
                                + Vector3.up * DesplazamientoY.Value
                                + haciaFuera * DesplazamientoZ.Value;

                // Colocamos EL BOTON en ese punto, no la raiz del pedestal.
                //
                // El boton es un hijo situado a la altura del mostrador. Si movemos
                // la raiz, el boton acaba flotando a la altura a la que estaria el
                // mueble, que ahora esta oculto. Compensando el desfase, el punto que
                // calculamos es exactamente donde queda el boton.
                // Con el pato activado, el pedestal entero sobra.
                if (UsarPato.Value)
                {
                    estacion.gameObject.SetActive(false);
                    CrearPato(EstadoDe(punto), destino);
                    return;
                }

                // Anclamos por la placa roja: es la pieza que el jugador pulsa, asi
                // que es la que debe quedar a la altura indicada. La consola cuelga
                // de ella hacia abajo y apoya sola en el suelo.
                Transform referencia = null;
                foreach (Transform hijo in estacion)
                    if (hijo.gameObject.activeSelf &&
                        string.Equals(hijo.name, "Shop Button", StringComparison.OrdinalIgnoreCase))
                    { referencia = hijo; break; }

                if (referencia == null)
                    foreach (Transform hijo in estacion)
                        if (hijo.gameObject.activeSelf) { referencia = hijo; break; }

                Vector3 desfase = referencia != null
                                ? referencia.position - estacion.position
                                : Vector3.zero;

                estacion.position = destino - desfase;

                Plugin.Debug($"Pieza de referencia: {(referencia == null ? "ninguna" : referencia.name)}, " +
                             $"desfase={desfase.ToString("F2")}");

                Plugin.Debug(
                    $"Referencia: rampa={(punto.ramp == null ? "NULA" : punto.ramp.position.ToString("F1"))} " +
                    $"plataforma={(punto.platform == null ? "NULA" : punto.platform.position.ToString("F1"))} " +
                    $"haciaFuera={haciaFuera.ToString("F2")} lateral={lateral.ToString("F2")}");

                Plugin.Debug(
                    $"Pedestal '{estacion.name}': colocado en {destino.ToString("F1")} " +
                    $"(extractor en {punto.transform.position.ToString("F1")}) " +
                    $"activoEnJerarquia={estacion.gameObject.activeInHierarchy} " +
                    $"escala={estacion.lossyScale.ToString("F2")} " +
                    $"renderers={total} (reactivados {reactivados}) hijos={estacion.childCount}");

                foreach (Transform hijo in estacion)
                    Plugin.Debug($"   hijo '{hijo.name}' activo={hijo.gameObject.activeSelf}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Fallo al desplegar el pedestal: {e.Message}");
            }
        }

        // =====================================================================
        // EL PATO
        //
        // Alternativa al boton de la tienda: un pato de goma fijo en el aire junto
        // al extractor. Agarrarlo lanza la extraccion.
        //
        // Funciona porque PhysGrabObject.GrabStarted avisa al anfitrion por la red
        // del propio juego (RPC "GrabStartedRPC" con RpcTarget.MasterClient), asi
        // que basta con que el master vigile la lista playerGrabbing del pato.
        // =====================================================================

        /// <summary>
        /// Marca que viaja en los datos de instanciacion de Photon. Sirve para que
        /// CADA cliente reconozca "su" copia del pato y la desactive: el objeto se
        /// crea en todas las maquinas, asi que desnudarlo solo en el anfitrion
        /// dejaria a los demas viendo un pato que salta y grazna.
        /// </summary>
        private const string MarcaPato = "RepoAmigos:BotonPato";

        private static Item _itemPato;
        private static bool _patoBuscado;

        /// <summary>
        /// Le quita al pato todo lo que no sea "estar ahi para que lo agarren":
        /// saltos, graznidos, estelas, explosion, collider de dano, bateria y
        /// logica de item. Se queda solo la parte fisica que permite agarrarlo.
        /// </summary>
        internal static void DesnudarPato(GameObject pato)
        {
            if (pato == null) return;

            // Comportamientos concretos. No vale con apagar todos los MonoBehaviour:
            // PhysGrabObject y sus auxiliares son justo lo que necesitamos vivo.
            // OJO: ItemAttributes NO va en esta lista. Es quien guarda la identidad
            // del objeto para el texto de interaccion; al destruirlo, el juego
            // mostraba un nombre residual de otro item ("ROLL DRONE").
            string[] sobran =
            {
                "ItemRubberDuck", "HurtCollider", "ItemEquippable", "ItemBattery",
                "ItemToggle", "ParticleScriptExplosion", "ItemDeactivatedUntilLevel"
            };

            // DESTRUIR, no solo desactivar. ItemEquippable ya ha creado su cubo de
            // equipar y su texto "[E]", e ItemBattery su barra de carga: apagar el
            // componente detiene la logica pero deja lo ya dibujado en pantalla.
            foreach (MonoBehaviour comp in pato.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp == null) continue;
                string nombre = comp.GetType().Name;
                foreach (string s in sobran)
                    if (nombre == s) { UnityEngine.Object.Destroy(comp); break; }
            }

            // MapCustom lo pinta en el mapa como si fuera un objeto recogible.
            foreach (MapCustom mapa in pato.GetComponentsInChildren<MapCustom>(true))
                UnityEngine.Object.Destroy(mapa);

            // El indicador de bateria no es un componente sino un objeto aparte
            // colgado del item, asi que apagar ItemBattery no lo hace desaparecer.
            foreach (ItemBattery bateria in pato.GetComponentsInChildren<ItemBattery>(true))
                if (bateria.batteryTransform != null)
                    bateria.batteryTransform.gameObject.SetActive(false);

            foreach (MonoBehaviour comp in pato.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp == null) continue;
                string nombre = comp.GetType().Name;
                if (nombre == "BatteryUI" || nombre == "ItemEquipCube")
                    comp.gameObject.SetActive(false);
            }

            // El cubo flotante de equipar es un objeto aparte. Se destruye entero,
            // no basta con quitarle el componente.
            foreach (MonoBehaviour comp in pato.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp == null) continue;
                string nombre = comp.GetType().Name;
                if (nombre == "ItemEquipCube" || nombre == "BatteryUI")
                    UnityEngine.Object.Destroy(comp.gameObject);
            }

            // NADA de podas automaticas por tamano de malla.
            //
            // Lo intente y salio mal: el pato lleva dentro una version "rota"
            // desactivada, y al medir tambien los renderers inactivos sus trozos
            // ganaban en tamano. Resultado: apagaba la rama buena y el pato
            // desaparecia. Aqui solo se quita lo que se sabe que sobra.

            foreach (Animator a in pato.GetComponentsInChildren<Animator>(true))
                a.enabled = false;

            foreach (AudioSource a in pato.GetComponentsInChildren<AudioSource>(true))
            { a.Stop(); a.enabled = false; }

            foreach (ParticleSystem p in pato.GetComponentsInChildren<ParticleSystem>(true))
                p.Stop();

            foreach (TrailRenderer t in pato.GetComponentsInChildren<TrailRenderer>(true))
                t.enabled = false;

            // ================================================================
            // EL PATO Y LOS 80.000 AVISOS DE "kinematic body"
            // ================================================================
            //
            //     Setting linear velocity of a kinematic body is not supported.
            //     Setting angular velocity of a kinematic body is not supported.
            //
            // Dos por frame mientras exista el pato: 80.000 lineas y 5 MB de
            // Player.log en una sesion. Visualmente no rompe nada — el pato se
            // queda quieto igual — pero cada LogWarning de Unity captura la pila
            // de llamadas, asi que son dos capturas por frame: tirones, y el ruido
            // sepultando cualquier linea util para depurar.
            //
            // La causa esta en ItemEquippable.Update, y es un fallo del juego:
            //
            //     itemEquippable.teleportPositionForcedTimer -= Time.deltaTime;
            //     if (rb.isKinematic) {              // <-- SOLO si es cinematico
            //         rb.velocity        = Vector3.zero;
            //         rb.angularVelocity = Vector3.zero;
            //     }
            //
            // O sea que pone la velocidad a cero justo en el unico caso en que
            // Unity no lo permite. En una partida normal apenas se nota porque los
            // items pasan poco tiempo en ese estado; nuestro pato se queda ahi el
            // nivel entero.
            //
            // INTENTO FALLIDO, para que no se repita: se probo a dejar el cuerpo NO
            // cinematico (isKinematic = false + RigidbodyConstraints.FreezeAll),
            // que sobre el papel es correcto — asi la escritura es legal y las
            // restricciones anulan la velocidad. No sirvio de nada. PhysGrabObject
            // tiene toda una bateria de metodos (OverrideKinematic,
            // OverrideKinematicLogic, OverrideTimersTick, OverrideDeactivate...)
            // que reimponen isKinematic continuamente, asi que un cambio de una
            // sola vez al crear el pato lo pisan al instante. Esa pelea no se gana.
            //
            // La solucion va a la causa: el pato es un boton decorativo, nunca se
            // equipa, asi que ItemEquippable no le hace ninguna falta. Apagando el
            // componente desaparece el unico escritor por frame que dispara con el
            // cuerpo cinematico, y da igual quien mande sobre isKinematic.
            //
            // SEGUNDO INTENTO FALLIDO, tambien anotado para no repetirlo: se probo
            // `eq.enabled = false`. Sobre el papel basta, porque Unity no llama al
            // Update de un componente desactivado. En la practica seguian saliendo
            // 15.902 avisos: algo lo vuelve a activar (el item se "reequipa" solo
            // en algun punto de su ciclo de vida).
            //
            // Lo que si es inmune a eso es interceptar el metodo con Harmony: da
            // igual quien active o desactive el componente, el Update no se ejecuta.
            // Ver el parche SilenciarEquipableDelPato mas abajo.
            //
            // Se sigue desactivando porque no cuesta nada y ahorra trabajo cuando
            // funciona; el parche es la garantia.
            foreach (ItemEquippable eq in pato.GetComponentsInChildren<ItemEquippable>(true))
            {
                eq.enabled = false;
                _equipablesDelPato.Add(eq);
            }

            Plugin.Debug($"Pato: {_equipablesDelPato.Count} ItemEquippable silenciados.");

            // Quieto en el aire: es un boton, no un juguete.
            foreach (Rigidbody rb in pato.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity  = false;
            }

            RegistrarPato(pato);

            // Volcado de lo que cuelga del pato, para poder cazar cualquier cartel
            // que siga apareciendo sin tener que ir a ciegas.
            if (Plugin.VerboseLogging != null && Plugin.VerboseLogging.Value)
            {
                var visibles = new List<string>();
                foreach (Renderer r in pato.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled && r.gameObject.activeInHierarchy)
                        visibles.Add($"{r.gameObject.name}({r.GetType().Name})");

                var comps = new List<string>();
                foreach (MonoBehaviour c in pato.GetComponentsInChildren<MonoBehaviour>(true))
                    if (c != null) comps.Add($"{c.GetType().Name}@{c.gameObject.name}");

                Plugin.Debug($"Pato desnudado. Piezas visibles: {string.Join(", ", visibles.ToArray())}");
                Plugin.Debug($"Componentes que quedan: {string.Join(", ", comps.ToArray())}");

                // Que hay alrededor del pato. Sirve para identificar el cubo con la
                // X rosa y saber si cuelga del pato o es un objeto independiente.
                var cerca = new List<string>();
                foreach (ItemEquipCube cubo in UnityEngine.Object.FindObjectsOfType<ItemEquipCube>())
                {
                    float d = Vector3.Distance(cubo.transform.position, pato.transform.position);
                    if (d > 4f) continue;

                    Transform raiz = cubo.transform.root;
                    bool esDelPato = cubo.transform.IsChildOf(pato.transform);
                    cerca.Add($"'{cubo.gameObject.name}' raiz='{raiz.name}' a {d:F1}m " +
                              $"{(esDelPato ? "(del pato)" : "(AJENO)")}");
                }

                Plugin.Debug(cerca.Count == 0
                    ? "No hay ningun ItemEquipCube cerca del pato."
                    : $"Cubos de equipar cerca: {string.Join(" | ", cerca.ToArray())}");
            }
        }

        /// <summary>Enlace pato -> extractor al que pertenece, en cada cliente.</summary>
        private sealed class Vinculo { public ExtractionPoint Punto; }

        private static readonly ConditionalWeakTable<PhysGrabObject, Vinculo> _patos =
            new ConditionalWeakTable<PhysGrabObject, Vinculo>();

        private static readonly System.Reflection.FieldInfo _photonViewExtractor =
            AccessTools.Field(typeof(ExtractionPoint), "photonView");

        /// <summary>
        /// Apunta el pato en el registro y lo asocia a su extractor.
        /// En los clientes el pato lo crea Photon, asi que no sabemos de que
        /// extractor es: lo deducimos por cercania.
        /// </summary>
        private static void RegistrarPato(GameObject pato)
        {
            PhysGrabObject fisico = pato.GetComponent<PhysGrabObject>();
            if (fisico == null) return;

            ExtractionPoint mejor = null;
            float minimo = float.MaxValue;

            foreach (ExtractionPoint p in UnityEngine.Object.FindObjectsOfType<ExtractionPoint>())
            {
                float d = Vector3.Distance(p.transform.position, pato.transform.position);
                if (d < minimo) { minimo = d; mejor = p; }
            }

            Vinculo previo;
            if (_patos.TryGetValue(fisico, out previo)) previo.Punto = mejor;
            else _patos.Add(fisico, new Vinculo { Punto = mejor });

            Plugin.Debug($"Pato registrado, extractor mas cercano a {minimo:F1} m.");
        }

        private static float _ultimoAvisoPato = -10f;

        /// <summary>
        /// Dice EN PANTALLA por que el pato no ha hecho nada.
        ///
        /// Antes esto solo se escribia en el log de depuracion, asi que desde el
        /// juego era indistinguible de que el mod estuviera roto: se pulsa y no
        /// pasa nada. En una partida real se pulso 114 veces seguidas sin que el
        /// jugador pudiera saber que le faltaba.
        ///
        /// El motivo hay que separarlo, porque se arreglan de forma distinta:
        /// que el extractor no este activo se resuelve entrando en el, y que falte
        /// botin se resuelve trayendo mas cosas.
        /// </summary>
        private static void AvisarPorQueNoSePuede(Estado estado)
        {
            string motivo;
            if (!estado.Activo)
                motivo = "Este extractor todavia no esta activo";
            else if (!estado.MetaConocida)
                motivo = "Espera, el extractor aun no ha pedido su botin";
            else
                motivo = $"Faltan {estado.FaltaBotin:N0} de botin";

            // Un agarre mantenido vuelve a disparar GrabStarted, asi que sin freno
            // el mensaje parpadearia. El freno cubre tambien la traza: en la sesion
            // de las 114 pulsaciones se escribieron 114 lineas para no decir nada.
            if (Time.time - _ultimoAvisoPato < 1.5f) return;
            _ultimoAvisoPato = Time.time;

            Plugin.Debug($"Pato pulsado y no se puede: {motivo}.");
            SemiFunc.UIFocusText(motivo, new Color(1f, 0.6f, 0.2f), Color.white, 3f);
        }

        /// <summary>
        /// El pato es un boton, no un objeto que llevarse. Cancelamos el agarre y
        /// lo tratamos como pulsacion.
        ///
        /// Devolver false aqui impide ademas el RPC interno de GrabStarted, asi que
        /// la peticion al anfitrion la mandamos nosotros por el mismo canal que ya
        /// usa el boton normal.
        /// </summary>
        [HarmonyPatch(typeof(PhysGrabObject), "GrabStarted")]
        private static class PatoNoSeLevanta
        {
            private static bool Prefix(PhysGrabObject __instance)
            {
                if (!Enabled.Value || !UsarPato.Value) return true;

                Vinculo vinculo;
                if (!_patos.TryGetValue(__instance, out vinculo)) return true;   // no es nuestro pato

                if (vinculo.Punto != null)
                {
                    if (EstadoDe(vinculo.Punto).Esperando)
                    {
                        PhotonView vista = _photonViewExtractor?.GetValue(vinculo.Punto) as PhotonView;
                        Plugin.Debug("Pato pulsado: lanzando la extraccion.");
                        Solicitar(vinculo.Punto, vista);
                    }
                    else
                    {
                        AvisarPorQueNoSePuede(EstadoDe(vinculo.Punto));
                    }
                }

                return false;   // nunca se agarra ni se levanta
            }
        }

        /// <summary>
        /// Los ItemEquippable de los patos que hemos creado. Se comparan por
        /// referencia, asi que un HashSet basta y la busqueda es O(1) — importante,
        /// porque esto se consulta una vez por frame y por objeto equipable.
        /// </summary>
        private static readonly HashSet<ItemEquippable> _equipablesDelPato =
            new HashSet<ItemEquippable>();

        /// <summary>
        /// EL PARCHE QUE MATA LOS AVISOS DE "kinematic body".
        ///
        /// ItemEquippable.Update hace, y es un fallo del juego:
        ///
        ///     teleportPositionForcedTimer -= Time.deltaTime;
        ///     if (rb.isKinematic) {              // &lt;-- SOLO si es cinematico
        ///         rb.velocity        = Vector3.zero;
        ///         rb.angularVelocity = Vector3.zero;
        ///     }
        ///
        /// Escribe la velocidad justo en el unico caso en que Unity lo prohibe, y
        /// responde con dos LogWarning por frame. En una partida normal se nota
        /// poco porque los items pasan poco tiempo asi; nuestro pato se queda ahi
        /// el nivel entero: 80.000 lineas y 5 MB de Player.log en una sesion.
        ///
        /// Se comprobo con un barrido sobre todo Assembly-CSharp que este es el
        /// UNICO metodo por frame que escribe las dos velocidades entre los
        /// componentes del pato. PhysGrabObject.FixedUpdate y FreezeForces tambien
        /// las escriben, pero los dos empiezan comprobando isKinematic y salen.
        ///
        /// Solo se silencia en NUESTROS patos. Los items normales del juego siguen
        /// funcionando igual: son los mismos que usan los jugadores para equiparse.
        /// </summary>
        [HarmonyPatch(typeof(ItemEquippable), "Update")]
        private static class SilenciarEquipableDelPato
        {
            private static bool Prefix(ItemEquippable __instance)
            {
                if (!Enabled.Value || _equipablesDelPato.Count == 0) return true;
                return !_equipablesDelPato.Contains(__instance);
            }
        }

        /// <summary>
        /// Cada cliente desactiva su propia copia del pato en cuanto arranca.
        /// </summary>
        [HarmonyPatch(typeof(ItemRubberDuck), "Start")]
        private static class DesnudarPatoEnCadaCliente
        {
            private static void Postfix(ItemRubberDuck __instance)
            {
                PhotonView vista = __instance.GetComponent<PhotonView>();
                object[] datos = vista != null ? vista.InstantiationData : null;

                if (datos == null || datos.Length == 0) return;
                if (!(datos[0] is string marca) || marca != MarcaPato) return;

                DesnudarPato(__instance.gameObject);
            }
        }

        /// <summary>
        /// Suena el boton del extractor al lanzarse la extraccion. Va en StateSetRPC
        /// porque ese si se ejecuta en TODAS las maquinas, no solo en el anfitrion.
        /// </summary>
        [HarmonyPatch(typeof(ExtractionPoint), "StateSetRPC")]
        private static class SonidoAlPulsar
        {
            private static void Prefix(ExtractionPoint __instance, ExtractionPoint.State state, bool ___isShop)
            {
                if (!Enabled.Value || ___isShop) return;
                if (state != ExtractionPoint.State.Success) return;

                try
                {
                    Estado estado = EstadoDe(__instance);
                    Vector3 donde = estado.Pato != null
                                  ? estado.Pato.transform.position
                                  : __instance.transform.position;

                    if (__instance.soundButton != null)
                        __instance.soundButton.Play(donde, 1f, 1f, 1f, 1f);
                }
                catch { /* el sonido no debe romper nunca la extraccion */ }
            }
        }

        /// <summary>Busca el item del pato entre los ScriptableObject del juego. Se cachea.</summary>
        private static Item BuscarItemPato()
        {
            if (_patoBuscado) return _itemPato;
            _patoBuscado = true;

            string buscado = (NombrePato.Value ?? "").Trim();

            try
            {
                var candidatos = new List<Item>();

                foreach (Item item in Resources.LoadAll<Item>(""))
                {
                    if (item == null || string.IsNullOrEmpty(item.itemName)) continue;

                    // Coincidencia exacta: manda sobre cualquier otra.
                    if (string.Equals(item.itemName, buscado, StringComparison.OrdinalIgnoreCase))
                    {
                        _itemPato = item;
                        break;
                    }

                    if (item.itemName.IndexOf("duck", StringComparison.OrdinalIgnoreCase) >= 0)
                        candidatos.Add(item);
                }

                // Sin coincidencia exacta, tiramos del primero que contenga "duck".
                // Antes esto cogia 'Duck Bucket', que no es lo que se busca.
                if (_itemPato == null && candidatos.Count > 0)
                    _itemPato = candidatos[0];

                if (candidatos.Count > 0)
                {
                    var nombres = new List<string>();
                    foreach (Item c in candidatos) nombres.Add($"'{c.itemName}'");
                    Plugin.Debug($"Items tipo pato disponibles: {string.Join(", ", nombres.ToArray())}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Fallo buscando el item del pato: {e.Message}");
            }

            if (_itemPato == null)
                Plugin.Log.LogWarning($"No encontre ningun item llamado '{buscado}' ni con 'duck' en el nombre.");
            else
                Plugin.Debug($"Item del pato: '{_itemPato.itemName}' (buscaba '{buscado}').");

            return _itemPato;
        }

        /// <summary>Crea el pato en el sitio indicado. Solo master o un jugador.</summary>
        private static void CrearPato(Estado estado, Vector3 posicion)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (estado.Pato != null) return;

            Item item = BuscarItemPato();
            if (item == null || item.prefab == null) return;

            // Orientacion: el prefab del pato no viene derecho. El juego usa
            // Item.spawnRotationOffset para enderezarlo al colocarlo en la tienda;
            // instanciarlo con Quaternion.identity lo dejaba tumbado.
            Quaternion orientacion = item.spawnRotationOffset
                                   * Quaternion.Euler(GiroPatoX.Value, GiroPatoY.Value, GiroPatoZ.Value);

            GameObject objeto;
            if (GameManager.instance.gameMode == 0)
            {
                objeto = UnityEngine.Object.Instantiate(item.prefab.Prefab, posicion, orientacion);
                // Un jugador: no hay Photon ni datos de instanciacion, lo desnudamos aqui.
                DesnudarPato(objeto);
            }
            else
            {
                // La marca viaja a todos los clientes para que cada uno desactive
                // su copia (ver DesnudarPatoEnCadaCliente).
                objeto = PhotonNetwork.InstantiateRoomObject(
                    item.prefab.ResourcePath, posicion, orientacion, 0,
                    new object[] { MarcaPato });
            }

            if (objeto == null)
            {
                Plugin.Log.LogWarning("No se pudo crear el pato.");
                return;
            }

            objeto.transform.localScale *= EscalaPato.Value;
            estado.Pato = objeto.GetComponent<PhysGrabObject>();

            // Guardamos su sitio: el cuerpo del jugador empuja los objetos fisicos
            // (PlayerPhysPusher), asi que hay que devolverlo si lo mueven.
            estado.PatoPosicion = posicion;
            estado.PatoRotacion = orientacion;

            // Volcado de que se ha creado realmente: el nombre del prefab y sus
            // componentes de nivel raiz. Sirve para cazar el caso de que el item
            // 'Rubber Duck' apunte a un prefab que no es el que esperamos.
            if (Plugin.VerboseLogging != null && Plugin.VerboseLogging.Value)
            {
                var comps = new List<string>();
                foreach (Component c in objeto.GetComponents<Component>())
                    if (c != null) comps.Add(c.GetType().Name);

                Plugin.Debug($"Prefab creado: '{objeto.name}' (ruta '{item.prefab.ResourcePath}'). " +
                             $"Componentes: {string.Join(", ", comps.ToArray())}");
            }

            Plugin.Debug($"Pato colocado en {posicion.ToString("F1")} " +
                         $"(PhysGrabObject={(estado.Pato == null ? "NULO" : "ok")}).");
        }

        /// <summary>Quita el pato cuando el extractor ya no se va a usar mas.</summary>
        private static void RetirarPato(Estado estado)
        {
            if (estado.Pato == null) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            GameObject objeto = estado.Pato.gameObject;
            estado.Pato = null;

            if (SemiFunc.IsMultiplayer()) PhotonNetwork.Destroy(objeto);
            else                          UnityEngine.Object.Destroy(objeto);

            Plugin.Debug("Pato retirado.");
        }

        /// <summary>Sustituye al Destroy de Start. Ver ConservarBotonDeTienda.</summary>
        internal static void DestruirSalvoQueLoConservemos(UnityEngine.Object objeto)
        {
            if (Enabled != null && Enabled.Value && BotonGrande != null && BotonGrande.Value)
                return;                                       // lo conservamos

            UnityEngine.Object.Destroy(objeto);               // comportamiento original
        }

        // =====================================================================
        // PARCHE 1 — bloquear el Success automatico
        // =====================================================================

        [HarmonyPatch(typeof(ExtractionPoint), "StateSet")]
        private static class BloquearSuccessAutomatico
        {
            // OJO: el parametro se llama `newState` en el juego. Harmony empareja por
            // nombre, asi que renombrarlo aqui hace fallar el parche en arranque.
            private static bool Prefix(ExtractionPoint __instance, ExtractionPoint.State newState, bool ___isShop)
            {
                if (!Enabled.Value) return true;
                if (newState != ExtractionPoint.State.Success) return true;
                if (___isShop) return true;      // la tienda tiene su propia logica, no se toca

                Estado estado = EstadoDe(__instance);

                if (estado.Armado)
                {
                    // Alguien pulso el boton: dejamos pasar la extraccion de verdad.
                    estado.Armado = false;
                    estado.Esperando = false;
                    estado.AvisoMostrado = false;
                    Plugin.Debug("Extraccion autorizada por pulsacion del boton.");
                    return true;
                }

                // Meta cubierta pero nadie ha pulsado: se queda esperando.
                return false;
            }
        }

        // =====================================================================
        // PARCHE 2 — mantener el boton usable y avisar en pantalla
        //
        // ButtonToggle hace `if (currentState != State.Idle) buttonGrabObject.enabled = false;`
        // o sea, en vanilla el boton deja de ser agarrable en cuanto el extractor
        // se activa. Hay que devolverle la vida mientras esperamos.
        // =====================================================================

        [HarmonyPatch(typeof(ExtractionPoint), "Update")]
        private static class MantenerBotonUsable
        {
            private static void Postfix(
                ExtractionPoint __instance,
                bool ___isShop,
                bool ___haulGoalFetched,
                int  ___haulCurrent,
                ExtractionPoint.State ___currentState)
            {
                if (!Enabled.Value) return;

                AsegurarRed();

                Estado estado = EstadoDe(__instance);

                bool metaCubierta = ___haulGoalFetched && (__instance.haulGoal - ___haulCurrent) <= 0;
                bool esperando    = !___isShop
                                 && ___currentState == ExtractionPoint.State.Active
                                 && metaCubierta;

                estado.Esperando = esperando;

                // Desglose para el aviso al pulsar el pato. Con Esperando a secas
                // no se puede distinguir "aun no has activado el extractor" de
                // "te falta botin", y son dos cosas que se arreglan distinto.
                estado.Activo       = ___currentState == ExtractionPoint.State.Active;
                estado.MetaConocida = ___haulGoalFetched;
                estado.FaltaBotin   = __instance.haulGoal - ___haulCurrent;

                // Una vez completada la extraccion, este extractor ya no se usa mas:
                // el boton se retira para no dejar mobiliario suelto por el nivel.
                if (___currentState == ExtractionPoint.State.Complete && !estado.YaCompletado)
                {
                    estado.YaCompletado = true;
                    if (estado.Estacion != null) estado.Estacion.gameObject.SetActive(false);
                    RetirarPato(estado);
                    Plugin.Debug("Extraccion completada: boton/pato retirado.");
                }

                // El pato no se mueve del sitio. Que sea cinematico no basta: el
                // cuerpo del jugador lo empuja igualmente, asi que lo devolvemos a
                // su posicion en cuanto se desvia. Al ser objeto de sala del
                // anfitrion, los demas jugadores lo ven quieto por sincronizacion.
                if (estado.Pato != null && SemiFunc.IsMasterClientOrSingleplayer())
                {
                    Transform t = estado.Pato.transform;

                    if ((t.position - estado.PatoPosicion).sqrMagnitude > 0.0004f)
                    {
                        t.position = estado.PatoPosicion;
                        t.rotation = estado.PatoRotacion;
                    }

                    // Un cuerpo cinematico ignora la velocidad: Unity lo mueve solo
                    // por transform. Escribirsela no hace nada util y ademas no esta
                    // permitido, asi que aqui solo se toca mientras siga siendo
                    // dinamico, y el orden importa — primero poner a cero, despues
                    // volverlo cinematico.
                    //
                    // Antes se hacia al reves (isKinematic = true y a continuacion
                    // las dos velocidades), que es exactamente el fallo que este
                    // mismo archivo le achaca a ItemEquippable.Update mas arriba.
                    // Salian dos LogWarning por frame y por pato, cada uno con su
                    // captura de pila: 356.020 lineas y 22 MB de Player.log en una
                    // sola sesion de cinco patos, y los tirones que iban con ello.
                    Rigidbody cuerpo = estado.Pato.GetComponent<Rigidbody>();
                    if (cuerpo != null && !cuerpo.isKinematic)
                    {
                        cuerpo.velocity        = Vector3.zero;
                        cuerpo.angularVelocity = Vector3.zero;
                        cuerpo.isKinematic     = true;
                    }
                }

                // El pato hace de boton: agarrarlo lanza la extraccion.
                //
                // Solo mira el master. PhysGrabObject.GrabStarted avisa al anfitrion
                // por RPC, asi que su playerGrabbing se llena aunque quien agarre sea
                // otro jugador, sin necesidad de red propia.
                if (esperando && estado.Pato != null && SemiFunc.IsMasterClientOrSingleplayer())
                {
                    var agarrando = estado.Pato.playerGrabbing;
                    if (agarrando != null && agarrando.Count > 0)
                    {
                        Plugin.Debug("Pato agarrado: lanzando la extraccion.");
                        Ejecutar(__instance);
                    }
                }

                if (!esperando)
                {
                    // Devolver la luz a como estaba si se la habiamos cambiado.
                    if (estado.LuzIntensidad >= 0f && __instance.buttonLight != null)
                    {
                        __instance.buttonLight.intensity = estado.LuzIntensidad;
                        __instance.buttonLight.color = estado.LuzColor;
                        estado.LuzIntensidad = -1f;
                    }
                    estado.AvisoMostrado = false;
                    return;
                }

                // Devolver el boton al estado usable. Se hace cada frame a proposito:
                // el juego lo vuelve a apagar en cuanto llama a ButtonToggle.
                if (__instance.buttonGrabObject != null && !__instance.buttonGrabObject.enabled)
                {
                    __instance.buttonGrabObject.enabled = true;
                    Plugin.Debug("Boton del extractor reactivado (estaba desactivado).");
                }

                if (__instance.buttonLight != null && !__instance.buttonLight.enabled)
                    __instance.buttonLight.enabled = true;

                // Traza de diagnostico, una vez por espera: si el mod frena la
                // extraccion pero no encuentras el boton, esto dice si existe.
                if (!estado.AvisoMostrado)
                {
                    Plugin.Debug(
                        $"Meta cubierta ({___haulCurrent}/{__instance.haulGoal}). Esperando pulsacion. " +
                        $"buttonGrabObject={(__instance.buttonGrabObject == null ? "NULO" : "ok")} " +
                        $"buttonLight={(__instance.buttonLight == null ? "NULO" : "ok")} " +
                        $"pedestalRescatado={(estado.Estacion == null ? "NO" : "SI")}");

                    EncenderBoton(__instance, estado);
                }

                // Aviso en la pantalla del tubo, una sola vez por espera.
                if (AvisoEnPantalla.Value && !estado.AvisoMostrado)
                {
                    estado.AvisoMostrado = true;
                    try
                    {
                        _tubeScreenTextChange.Invoke(
                            __instance,
                            new object[] { TextoPantalla.Value, Color.yellow });
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"No se pudo cambiar el texto del extractor: {e.Message}");
                    }
                }
            }
        }

        // =====================================================================
        // PARCHE 3 — reutilizar el boton para lanzar la extraccion
        //
        // OnClick vanilla solo sirve para activar un extractor en Idle; si el
        // estado es otro, se sale. Aqui le damos un segundo uso.
        // =====================================================================

        /// <summary>
        /// El boton grande de la tienda tiene su propio manejador. En vanilla:
        ///
        ///     if (!StateIs(Active) || tubeSlamDownEval &lt; 1f) return;
        ///     if (haulGoal - haulCurrent &gt;= 0 &amp;&amp; ...) StateSet(Success);
        ///     else StateSet(Cancel);
        ///
        /// Si lo dejamos pasar, su StateSet(Success) chocaria con nuestro bloqueo.
        /// Lo enrutamos por la misma via que el boton normal.
        /// </summary>
        [HarmonyPatch(typeof(ExtractionPoint), "OnShopClick")]
        private static class BotonTiendaLanzaExtraccion
        {
            private static bool Prefix(ExtractionPoint __instance, PhotonView ___photonView, bool ___isShop)
            {
                if (!Enabled.Value) return true;
                if (___isShop) return true;      // una tienda de verdad: no se toca

                if (EstadoDe(__instance).Esperando)
                {
                    Solicitar(__instance, ___photonView);
                    return false;
                }

                // El boton esta visible desde el principio, asi que se puede pulsar
                // antes de tiempo. Hay que cortarlo: el OnShopClick original haria
                // StateSet(Success) o StateSet(Cancel) sobre un extractor normal.
                // Mismo aviso que el pato: cortar en silencio se lee como averia.
                AvisarPorQueNoSePuede(EstadoDe(__instance));
                return false;
            }
        }

        [HarmonyPatch(typeof(ExtractionPoint), "OnClick")]
        private static class BotonLanzaExtraccion
        {
            private static bool Prefix(ExtractionPoint __instance, PhotonView ___photonView)
            {
                // Aqui habia una segunda comprobacion del rol de ingeniero, porque
                // el suyo era otro Prefix del mismo OnClick y el orden que elige
                // PatchAll no es determinista. Al quitarse el candado (1.5.0) sobra
                // tambien esta: ya no hay carrera que empatar.
                if (!Enabled.Value) return true;
                if (__instance.isLocked) return true;

                if (!EstadoDe(__instance).Esperando)
                    return true;     // comportamiento normal: activar el extractor

                Solicitar(__instance, ___photonView);
                return false;        // no ejecutar el OnClick original
            }
        }
    }
}
