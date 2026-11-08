using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;

namespace SPSave.Game
{
    /// <summary>
    /// BossNotifier (Mattexe.BossNotifier) lists the raid's bosses from a postfix on BossLocationSpawn.Init, which
    /// rolls each wave's chance and zone. On a resume LocalGame.Create first builds the server's fresh boss scenario
    /// (new rolls) and Waves.Restore then replaces it, so BossNotifier would announce bosses that rolled in the fresh
    /// scenario, not in the saved raid (2026-11-08: a Woods save without Goons announced Knight). After the restore its
    /// list is rebuilt from the saved raid's rolls through its own postfix, so names and zones read as in a fresh raid.
    /// Bosses alive at the save are restored as bots, and BossNotifier sees them spawn as usual.
    /// Members checked against BossNotifier 1.2.1.
    /// </summary>
    internal static class BossNotifierCompat
    {
        internal const string Guid = "Mattexe.BossNotifier";

        internal static void Rebuild(WavesState saved)
        {
            if (saved?.Boss == null || !Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null)
            {
                return;
            }
            try
            {
                var patch = info.Instance.GetType().Assembly.GetType("BossNotifier.BossLocationSpawnPatch");
                var list = patch != null ? AccessTools.Field(patch, "bossesInRaid")?.GetValue(null) as IDictionary : null;
                var postfix = patch != null ? AccessTools.Method(patch, "PatchPostfix", new[] { typeof(BossLocationSpawn) }) : null;
                if (list == null || postfix == null)
                {
                    Plugin.Log.LogWarning("BossNotifier is loaded but its boss list was not found; it may announce bosses the saved raid didn't have.");
                    return;
                }
                list.Clear();
                var added = 0;
                foreach (var state in saved.Boss)
                {
                    if (!state.ShallSpawn || string.IsNullOrEmpty(state.BossName) || string.IsNullOrEmpty(state.BornZone))
                    {
                        continue;
                    }
                    var wave = new BossLocationSpawn { BossName = state.BossName, BossZone = state.BossZone ?? "" };
                    wave.ParseBossName();
                    wave.ShallSpawn = true;
                    wave.BornZone = state.BornZone;
                    postfix.Invoke(null, new object[] { wave });
                    added++;
                }
                Plugin.Log.LogInfo($"BossNotifier: boss list set from the saved raid ({list.Count} bosses from {added} waves that rolled).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("BossNotifier boss list could not be set from the save: " + e.Message);
            }
        }
    }
}
