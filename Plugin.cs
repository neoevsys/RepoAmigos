using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RepoAmigos.Patches;
using System.Linq;
using System.Reflection;

namespace RepoAmigos
{
    /// <summary>
    /// Mod base para jugar R.E.P.O. con los amigos.
    /// Aqui solo va el arranque: cargar config y aplicar los parches Harmony.
    /// La logica de cada tweak vive en su propio archivo dentro de Patches/.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("REPO.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "com.usuario.repoamigos";
        public const string PluginName    = "RepoAmigos";
        public const string PluginVersion = "1.5.0";

        internal static ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            BindConfig();
            ExtractionOnDemand.BindConfig(Config);
            ReviveEnExtractor.BindConfig(Config);
            GeneradorDePruebas.BindConfig(Config);
            CarritoEncoge.BindConfig(Config);

            // Roles primero: los cuatro roles se suscriben a Roles.AlRepartir
            // dentro de su propio BindConfig, asi que el orden importa.
            ComprobacionDeVersion.BindConfig(Config);
            Roles.BindConfig(Config);
            RolMedico.BindConfig(Config);
            RolIngeniero.BindConfig(Config);
            RolSaboteador.BindConfig(Config);
            RolRastreador.BindConfig(Config);

            // PatchAll recoge todas las clases [HarmonyPatch] de este assembly.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.LogInfo($"{PluginName} v{PluginVersion} cargado. " +
                        $"Metodos parcheados: {_harmony.GetPatchedMethods().Count()}");
            Log.LogInfo($"Extraccion manual: {(ExtractionOnDemand.Enabled.Value ? "ACTIVADA" : "desactivada")}");
            Log.LogInfo($"Revivir en extractor: {(ReviveEnExtractor.Enabled.Value ? $"ACTIVADO ({ReviveEnExtractor.Segundos.Value}s)" : "desactivado")}");

            // Volcado del estado real de la config de pruebas. Si aqui sale
            // Activado=False o la tecla no es la esperada, el fallo es de config;
            // si sale bien, el fallo esta en la ejecucion por frame.
            Log.LogInfo($"Pruebas: Activado={GeneradorDePruebas.Enabled.Value}, " +
                        $"Tecla={GeneradorDePruebas.Tecla.Value}, " +
                        $"Cantidad={GeneradorDePruebas.Cantidad.Value}");

            Log.LogInfo(Roles.Enabled.Value
                ? $"Roles: ACTIVADOS  (minimo {Roles.MinJugadores.Value} jugadores)  " +
                  $"medico[{RolMedico.Tecla.Value}]={RolMedico.Enabled.Value} " +
                  $"ingeniero={RolIngeniero.Enabled.Value} " +
                  $"saboteador[{RolSaboteador.Tecla.Value}]={RolSaboteador.Enabled.Value} " +
                  $"rastreador={RolRastreador.Enabled.Value}"
                : "Roles: desactivados");
        }

        private void Update()
        {
            // BaseUnityPlugin es un MonoBehaviour, asi que aqui tenemos un Update
            // normal donde leer el teclado sin necesidad de parchear nada del juego.
            GeneradorDePruebas.Tick("Plugin");
        }

        private void OnDestroy()
        {
            // OJO: aqui NO se desparchea.
            //
            // Tenia un _harmony.UnpatchSelf() para poder recargar en caliente, pero
            // si Unity destruye este componente al cambiar de escena, ese Unpatch
            // borra los parches y deja el mod inerte el resto de la sesion: Harmony
            // informa de N metodos parcheados al arrancar y luego no se ejecuta
            // ninguno. Los parches deben sobrevivir a los cambios de escena.
            Log.LogWarning("OnDestroy: el componente del plugin se ha destruido. " +
                           "Los parches Harmony se mantienen a proposito.");
            ExtractionOnDemand.DesconectarRed();
            Roles.DesconectarRed();
        }

        // =====================================================================
        // Configuracion — se genera sola en:
        //   <perfil>\BepInEx\config\com.usuario.repoamigos.cfg
        // Se puede editar sin recompilar nada.
        // =====================================================================

        /// <summary>Activa los mensajes detallados en la consola de BepInEx.</summary>
        internal static ConfigEntry<bool> VerboseLogging;

        private void BindConfig()
        {
            VerboseLogging = Config.Bind(
                section:      "General",
                key:          "LogDetallado",
                defaultValue: false,
                description:  "Escribe informacion extra en la consola de BepInEx. Util para depurar, ruidoso para jugar.");
        }

        /// <summary>Log que solo aparece si LogDetallado esta activado.</summary>
        internal static void Debug(string mensaje)
        {
            if (VerboseLogging != null && VerboseLogging.Value)
                Log.LogInfo("[debug] " + mensaje);
        }
    }
}
