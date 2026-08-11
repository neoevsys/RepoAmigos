using BepInEx.Configuration;
using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RepoAmigos.Patches
{
    /// <summary>
    /// REVIVIR METIENDO LA CABEZA EN EL EXTRACTOR.
    ///
    /// En vanilla la cabeza de un muerto solo revive en dos sitios:
    ///
    ///  1. En el camion. PlayerDeathHead.Update hace:
    ///         if (inTruck) {
    ///             inTruckReviveTimer -= Time.deltaTime;
    ///             if (inTruckReviveTimer &lt;= 0f) playerAvatar.Revive(true);
    ///         } else inTruckReviveTimer = 2f;
    ///
    ///  2. En el extractor, pero SOLO cuando la extraccion ya se ha completado:
    ///     ExtractionPoint.DestroyAllPhysObjectsInHaulList llama a PlayerDeathHead.Revive().
    ///
    /// Este parche aplica la logica del camion al extractor: basta con dejar la
    /// cabeza dentro y esperar unos segundos, sin tener que completar nada.
    ///
    /// Reutilizamos el propio PlayerDeathHead.Revive() del juego en vez de llamar a
    /// PlayerAvatar.Revive(bool) a pelo, porque:
    ///   - es publico y ya comprueba `triggered &amp;&amp; inExtractionPoint &amp;&amp; playerAvatar`
    ///   - usa Revive(false), que revive al jugador en el sitio en vez de aplicar
    ///     el TruckHealer.Heal() que corresponde al camion
    ///
    /// Autoridad: PlayerAvatar.Revive hace photonView.RPC(..., RpcTarget.All), asi que
    /// solo puede llamarlo UNA maquina o se emiten RPCs duplicados. Por eso va detras
    /// de SemiFunc.IsMasterClientOrSingleplayer(), igual que el bloque vanilla del camion.
    /// </summary>
    internal static class ReviveEnExtractor
    {
        // =====================================================================
        // Configuracion
        // =====================================================================

        internal static ConfigEntry<bool>  Enabled;
        internal static ConfigEntry<float> Segundos;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "RevivirEnExtractor", "Activado", true,
                "Si esta activado, dejar la cabeza de un companero muerto dentro de un extractor " +
                "lo revive tras unos segundos, sin necesidad de completar la extraccion.");

            Segundos = config.Bind(
                "RevivirEnExtractor", "SegundosDeEspera", 2.0f,
                new ConfigDescription(
                    "Tiempo que la cabeza debe permanecer dentro del extractor antes de revivir. " +
                    "El camion usa 2 segundos en vanilla.",
                    new AcceptableValueRange<float>(0f, 30f)));
        }

        // =====================================================================
        // Estado por cabeza (ver nota sobre ConditionalWeakTable en ExtractionOnDemand)
        // =====================================================================

        private sealed class Estado
        {
            public float Temporizador = -1f;
            public bool  YaRevivido;
        }

        private static readonly ConditionalWeakTable<PlayerDeathHead, Estado> _estados =
            new ConditionalWeakTable<PlayerDeathHead, Estado>();

        // =====================================================================
        // Parche
        // =====================================================================

        [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
        private static class RevivirPorEstarDentro
        {
            private static void Postfix(
                PlayerDeathHead __instance,
                bool ___inExtractionPoint,
                bool ___triggered)
            {
                if (!Enabled.Value) return;

                // Igual que el bloque vanilla del camion: solo el master decide.
                if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

                Estado estado = _estados.GetOrCreateValue(__instance);

                // Fuera del extractor (o cabeza aun no activada): reiniciar la cuenta.
                if (!___inExtractionPoint || !___triggered)
                {
                    estado.Temporizador = Segundos.Value;
                    estado.YaRevivido   = false;
                    return;
                }

                if (estado.YaRevivido) return;

                if (estado.Temporizador < 0f)
                    estado.Temporizador = Segundos.Value;

                estado.Temporizador -= Time.deltaTime;
                if (estado.Temporizador > 0f) return;

                if (__instance.playerAvatar == null) return;

                // Pestillo antes de llamar: Revive() dispara un RPC a todos y la cabeza
                // tarda un frame o dos en dejar de estar "triggered".
                estado.YaRevivido = true;

                Plugin.Debug($"Reviviendo a {__instance.playerAvatar.name} desde el extractor.");
                __instance.Revive();
            }
        }
    }
}
