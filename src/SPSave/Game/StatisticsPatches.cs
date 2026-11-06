using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Counters;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    /// <summary>
    /// A resumed raid continues the saved raid's statistics session instead of starting a new one.
    ///
    /// Every raid start runs BaseStatisticsManager.BeginStatisticsSession (from Player.Init, while the player is
    /// created). For a new raid it clears EftStats.SessionCounters (this raid's kill, looting, healing and search
    /// XP, kills, ammo used: the raid-end XP and stats are computed from them), DamageHistory and Victims (the kill
    /// list on the results screen and for PMC chat responses). The game skips exactly those clears for a map-to-map
    /// transit (TransitController.IsTransit), which is what a resume is. So for the resumed player's profile, and only
    /// during that call, IsTransit answers yes; the game then keeps the saved counters, history and kill list itself.
    /// </summary>
    internal static class ContinuedSession
    {
        /// <summary>The profile whose statistics session is starting as a continuation, during BeginStatisticsSession only.</summary>
        internal static string ProfileId;
    }

    internal sealed class BeginStatisticsSessionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BaseStatisticsManager), nameof(BaseStatisticsManager.BeginStatisticsSession));

        [PatchPrefix]
        private static void Prefix(BaseStatisticsManager __instance)
        {
            ContinuedSession.ProfileId = ResumeSession.Active && ReferenceEquals(__instance.Profile, ResumeSession.Profile)
                ? __instance.Profile.Id
                : null;
        }

        [PatchPostfix]
        private static void Postfix(BaseStatisticsManager __instance)
        {
            if (ContinuedSession.ProfileId == null)
            {
                return;
            }
            ContinuedSession.ProfileId = null;
            var stats = __instance.Profile.EftStats;
            Plugin.Log.LogInfo($"Raid statistics carried on from the save: {stats.SessionCounters?.GetAllInt(CounterTag.Exp) ?? 0} session XP so far, " +
                               $"{stats.SessionCounters?.GetInt(PredefinedCounters.Kills) ?? 0} kills, {stats.Victims?.Count ?? 0} on the kill list.");
        }
    }

    /// <summary>
    /// Player.ManageGameQuests (BaseLocalGame.SpawnLoot, while loading) spawns quest loot, resets every quest counter
    /// that only counts within one raid (resetOnSessionEnd: "kill 5 in one raid") unless the player is in a transit,
    /// then checks transit conditions. A resume is the same raid: the same two steps run, without the reset. Quest loot
    /// comes from the saved world (location.Loot), so nothing spawns twice.
    /// </summary>
    internal sealed class ManageGameQuestsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.ManageGameQuests));

        [PatchPrefix]
        private static bool Prefix(Player __instance, ref Task __result)
        {
            if (!ResumeSession.Loading || !ReferenceEquals(__instance.Profile, ResumeSession.Profile))
            {
                return true;
            }
            __result = Continue(__instance);
            return false;
        }

        private static async Task Continue(Player player)
        {
            await player.GameWorld.ManageQuestLoot(player);
            player.QuestController?.CheckMapTransitConditions(player.GameWorld.TransitController);
            Plugin.Log.LogInfo("One-raid quest counters carried on from the save (not reset at raid start).");
        }
    }

    internal sealed class TransitContinuePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(TransitController), nameof(TransitController.IsTransit), new[] { typeof(string), typeof(int).MakeByRefType() });

        [PatchPrefix]
        private static bool Prefix(string profileId, ref int count, ref bool __result)
        {
            if (ContinuedSession.ProfileId == null || profileId != ContinuedSession.ProfileId)
            {
                return true;
            }
            count = 0;
            __result = true;
            return false;
        }
    }
}
