using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// GENERADOR DE OBJETOS PARA PRUEBAS (F3).
    ///
    /// Herramienta de desarrollo: crea objetos de valor delante de ti con un precio
    /// a medida, para poder cubrir la meta de un extractor en segundos y comprobar
    /// que la extraccion manual se comporta como debe, sin tener que jugar la ronda.
    ///
    /// Como lo hace el juego (ValuableDirector.SpawnValuable):
    ///
    ///     if (GameManager.instance.gameMode == 0)            // un jugador
    ///         go = Object.Instantiate(prefab, pos, rot);
    ///     else                                                // multijugador
    ///         go = PhotonNetwork.InstantiateRoomObject(resourcePath, pos, rot, 0, null);
    ///     go.GetComponent&lt;ValuableObject&gt;().DollarValueSetLogic();
    ///
    /// Y el precio (ValuableObject.DollarValueSetLogic):
    ///
    ///     if (dollarValueOverride != 0) {
    ///         dollarValueOriginal = dollarValueOverride;
    ///         dollarValueCurrent  = dollarValueOverride;
    ///     } else { ...valor aleatorio del preset... }
    ///
    /// O sea: basta con escribir `dollarValueOverride` ANTES de llamar a
    /// DollarValueSetLogic() y el objeto vale exactamente eso. Funciona porque
    /// Instantiate ejecuta Awake al momento pero Start (que es quien fija el valor
    /// normalmente) no corre hasta el frame siguiente: llegamos antes.
    /// </summary>
    internal static class GeneradorDePruebas
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>    Enabled;
        internal static ConfigEntry<KeyCode> Tecla;
        internal static ConfigEntry<int>     ValorMin;
        internal static ConfigEntry<int>     ValorMax;
        internal static ConfigEntry<int>     Cantidad;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "Pruebas", "Activado", true,
                "Permite generar objetos de valor con una tecla para probar el mod. " +
                "Ponlo en false cuando juegues en serio.");

            Tecla = config.Bind(
                "Pruebas", "Tecla", KeyCode.F3,
                "Tecla que genera los objetos.");

            ValorMin = config.Bind(
                "Pruebas", "ValorMinimo", 2000,
                new ConfigDescription("Precio minimo de cada objeto generado.",
                    new AcceptableValueRange<int>(1, 1000000)));

            ValorMax = config.Bind(
                "Pruebas", "ValorMaximo", 9000,
                new ConfigDescription("Precio maximo de cada objeto generado.",
                    new AcceptableValueRange<int>(1, 1000000)));

            Cantidad = config.Bind(
                "Pruebas", "CantidadPorPulsacion", 1,
                new ConfigDescription("Cuantos objetos salen con cada pulsacion.",
                    new AcceptableValueRange<int>(1, 20)));
        }

        // =====================================================================
        // Entrada por teclado — la llama Plugin.Update
        // =====================================================================

        private static bool _diagnosticoEscrito;

        /// <summary>
        /// REPO incluye Unity.InputSystem.dll. Con el Input System nuevo activo, el
        /// UnityEngine.Input clasico puede quedarse inerte SIN lanzar excepcion: no
        /// falla, simplemente devuelve false siempre. Por eso probamos los dos.
        /// </summary>
        private static bool TeclaPulsada()
        {
            // 1) Input System nuevo — el que usa realmente el juego
            try
            {
                Keyboard teclado = Keyboard.current;
                if (teclado != null &&
                    System.Enum.TryParse(Tecla.Value.ToString(), true, out Key codigo))
                {
                    var control = teclado[codigo];
                    if (control != null && control.wasPressedThisFrame) return true;
                }
            }
            catch { /* el Input System puede no estar inicializado todavia */ }

            // 2) Input clasico, por si el juego lo tuviera habilitado
            try
            {
                if (Input.GetKeyDown(Tecla.Value)) return true;
            }
            catch { /* lanza si el proyecto usa solo el Input System nuevo */ }

            return false;
        }

        private static int _teclasRegistradas;

        /// <summary>
        /// Con LogDetallado activado, anota las primeras teclas que se pulsen.
        /// Sirve para distinguir dos fallos que se parecen desde fuera:
        /// que el teclado no llegue al mod, o que llegue pero con otro nombre
        /// de tecla del que esperamos.
        /// </summary>
        private static void EspiarTeclado()
        {
            if (Plugin.VerboseLogging == null || !Plugin.VerboseLogging.Value) return;
            if (_teclasRegistradas >= 25) return;

            try
            {
                Keyboard teclado = Keyboard.current;
                if (teclado == null || !teclado.anyKey.wasPressedThisFrame) return;

                foreach (var control in teclado.allKeys)
                {
                    if (!control.wasPressedThisFrame) continue;
                    _teclasRegistradas++;
                    Plugin.Log.LogInfo(
                        $"Pruebas: tecla pulsada = {control.keyCode}  (la configurada es {Tecla.Value})");

                    if (_teclasRegistradas >= 25)
                    {
                        Plugin.Log.LogInfo("Pruebas: dejo de anotar teclas para no llenar el log.");
                        return;
                    }
                }
            }
            catch { }
        }

        private static bool LegacyOperativo()
        {
            try { Input.GetKeyDown(KeyCode.F13); return true; }
            catch { return false; }
        }

        /// <summary>
        /// Enganche de respaldo. El Update del propio plugin resulto no ejecutarse
        /// nunca (ni una linea de diagnostico, sin excepciones de por medio), asi que
        /// nos colgamos de un Update del juego que si corre seguro. RunManager existe
        /// durante toda la sesion, menu incluido.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), "Update")]
        private static class EngancheDeTeclado
        {
            private static bool _primeraVez = true;

            private static void Postfix()
            {
                // Traza incondicional: si esta linea no sale, el Postfix no corre
                // y el problema esta en el enganche, no dentro de Tick.
                if (_primeraVez)
                {
                    _primeraVez = false;
                    Plugin.Log.LogInfo("Pruebas: RunManager.Update esta corriendo. Enganche OK.");
                }
                Tick("RunManager");
            }
        }

        /// <summary>
        /// Sonda sobre un metodo que el juego llama con certeza: su mensaje
        /// "Changed level to:" aparece en el log. Si esta traza tampoco sale,
        /// el problema no es de los Update sino de que los parches no se aplican.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
        private static class SondaCambioDeNivel
        {
            private static void Postfix()
            {
                Plugin.Log.LogInfo("Pruebas: sonda ChangeLevel ejecutada. Los parches SI corren.");
            }
        }

        private static int _ultimoFrame = -1;

        internal static void Tick(string origen)
        {
            if (Enabled == null || !Enabled.Value) return;

            // Puede llamarnos tanto el Update del plugin como el de RunManager.
            // Sin esto, una pulsacion generaria dos objetos.
            if (Time.frameCount == _ultimoFrame) return;
            _ultimoFrame = Time.frameCount;

            // Una sola vez: confirma quien nos llama y que backend de input hay.
            if (!_diagnosticoEscrito)
            {
                _diagnosticoEscrito = true;
                bool nuevo = false;
                try { nuevo = Keyboard.current != null; } catch { }
                Plugin.Log.LogInfo(
                    $"Pruebas: activo via {origen}. Tecla configurada = {Tecla.Value}. " +
                    $"InputSystem nuevo = {(nuevo ? "SI" : "no")}, " +
                    $"Input clasico = {(LegacyOperativo() ? "SI" : "no")}.");
            }

            EspiarTeclado();

            if (!TeclaPulsada()) return;

            Plugin.Log.LogInfo($"Pruebas: {Tecla.Value} detectada.");

            // Spawnear objetos de red solo puede hacerlo el master, o se duplican
            // o se crean huerfanos que los demas no ven.
            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                Plugin.Log.LogWarning("F3: solo funciona si eres el anfitrion de la partida.");
                return;
            }

            if (ValuableDirector.instance == null)
            {
                Plugin.Log.LogWarning("F3: todavia no estas en un nivel.");
                return;
            }

            int total = 0;
            for (int i = 0; i < Cantidad.Value; i++)
                if (Generar(i)) total++;

            if (total > 0)
                Plugin.Log.LogInfo($"F3: generados {total} objeto(s) de prueba.");
        }

        // =====================================================================
        // Generacion
        // =====================================================================

        /// <summary>Listas de prefabs por tamano, en orden de preferencia (cargables a mano).</summary>
        private static readonly string[] _listasPorTamano =
        {
            "smallValuables", "tinyValuables", "mediumValuables"
        };

        private static List<PrefabRef> ElegirLista()
        {
            foreach (string nombre in _listasPorTamano)
            {
                var campo = AccessTools.Field(typeof(ValuableDirector), nombre);
                if (campo == null) continue;

                var lista = campo.GetValue(ValuableDirector.instance) as List<PrefabRef>;
                if (lista != null && lista.Count > 0) return lista;
            }
            return null;
        }

        private static bool Generar(int indice)
        {
            PlayerAvatar jugador = SemiFunc.PlayerAvatarLocal();
            if (jugador == null)
            {
                Plugin.Log.LogWarning("F3: no encuentro al jugador local.");
                return false;
            }

            List<PrefabRef> lista = ElegirLista();
            if (lista == null)
            {
                Plugin.Log.LogWarning("F3: las listas de objetos del nivel estan vacias.");
                return false;
            }

            PrefabRef prefabRef = lista[Random.Range(0, lista.Count)];

            // Delante del jugador y algo elevado, para que no aparezca dentro del suelo.
            // Con varios objetos los separamos en circulo para que no se empotren entre si.
            Transform t = jugador.transform;
            float angulo = indice * 0.9f;
            Vector3 desvio = new Vector3(Mathf.Sin(angulo), 0f, Mathf.Cos(angulo)) * 0.35f;
            Vector3 posicion = t.position + t.forward * 1.4f + Vector3.up * 1.1f + desvio;

            GameObject objeto;
            if (GameManager.instance.gameMode == 0)
                objeto = Object.Instantiate(prefabRef.Prefab, posicion, Quaternion.identity);
            else
                objeto = PhotonNetwork.InstantiateRoomObject(
                    prefabRef.ResourcePath, posicion, Quaternion.identity, 0, null);

            if (objeto == null)
            {
                Plugin.Log.LogWarning($"F3: no se pudo crear '{prefabRef.ResourcePath}'.");
                return false;
            }

            ValuableObject valioso = objeto.GetComponent<ValuableObject>();
            if (valioso == null)
            {
                Plugin.Log.LogWarning("F3: el prefab creado no tiene ValuableObject.");
                return false;
            }

            int precio = Random.Range(ValorMin.Value, ValorMax.Value + 1);

            // Escribimos el override ANTES de fijar el valor: gana sobre el preset.
            AccessTools.Field(typeof(ValuableObject), "dollarValueOverride")
                       .SetValue(valioso, precio);
            valioso.DollarValueSetLogic();

            // Los clientes calculan su propio valor aleatorio al instanciar, asi que
            // hay que decirles el nuestro o cada uno veria un precio distinto.
            if (SemiFunc.IsMultiplayer())
            {
                PhotonView vista = objeto.GetComponent<PhotonView>();
                if (vista != null)
                    vista.RPC("DollarValueSetRPC", RpcTarget.Others, (float)precio);
            }

            Plugin.Debug($"F3: '{prefabRef.PrefabName}' generado por ${precio}.");
            return true;
        }
    }
}
