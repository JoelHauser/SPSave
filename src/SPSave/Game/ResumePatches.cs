using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Bots;
using HarmonyLib;
using JsonType;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    // Every patch here does nothing unless a resume is in progress. All of them run during raid loading or start,
    // never per frame.

    /// <summary>
    /// Starts a resume: swaps in the saved profile at the start of LocalGameMatching, before it preloads
    /// Session.Profile.GetAllPrefabPaths(). Swapping later (LocalGameCreate, the first B1 build) left the saved gear's
    /// models unloaded: no weapon, a broken character.
    /// </summary>
    internal sealed class LocalGameMatchingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameMatching));

        [PatchPrefix]
        private static void Prefix(TarkovApplication __instance, bool inTransition)
        {
            if (inTransition)
            {
                ResumeSession.Reset();
                return;
            }
            ResumeSession.TryBegin(__instance);
        }
    }

    /// <summary>
    /// Restored corpses need their heads and clothing loaded; in a normal raid that happened when the bot spawned.
    /// They join the player's preload (LocalGameMatching) while a resume is loading.
    /// </summary>
    internal sealed class PrefabPathsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Profile), nameof(Profile.GetAllPrefabPaths));

        [PatchPostfix]
        private static void Postfix(Profile __instance, ref IEnumerable<ResourceKey> __result)
        {
            if (ResumeSession.Loading && ReferenceEquals(__instance, ResumeSession.Profile))
            {
                __result = __result.Concat(ResumeSession.CorpseBundles());
            }
        }
    }

    /// <summary>
    /// A raid that ends normally (extract, death, Leave, timer) can't be resumed: any save made during it is
    /// discarded, matched by the raid's serverId. Save &amp; Quit closes the game without ending the raid, so its save
    /// survives. A save waiting to be resumed belongs to an earlier raid and has a different serverId, so leaving a
    /// resumed raid before taking control keeps it.
    /// </summary>
    internal sealed class RaidEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Stop));

        [PatchPrefix]
        private static void Prefix(LocalGame __instance, ExitStatus exitStatus)
        {
            if (CaptureController.Quitting)
            {
                return;
            }
            try
            {
                var serverId = RaidCapture.RaidSettings(__instance)?.serverId;
                var path = Plugin.SavePath(__instance.Profile.Id);
                var sections = Core.SnapshotFile.LoadNewestValid(path, out _, null);
                if (string.IsNullOrEmpty(serverId) || sections == null ||
                    !sections.TryGetValue(RaidCapture.ManifestSection, out var bytes))
                {
                    return;
                }
                var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(System.Text.Encoding.UTF8.GetString(bytes));
                if (manifest?.ServerId != serverId)
                {
                    return;
                }
                var retired = Core.SnapshotFile.Retire(path, DateTime.UtcNow);
                Plugin.Log.LogInfo($"Raid ended normally ({exitStatus}), so its save can't be resumed; moved to {retired}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Checking for a save to discard at raid end failed: " + e);
            }
        }
    }

    /// <summary>The raid timer starts with the time already played, so the clock and the "run-through" rule are right.</summary>
    internal sealed class GameTimerStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(GameTimer), nameof(GameTimer.Start));

        [PatchPrefix]
        private static void Prefix(GameTimer __instance, ref DateTime? startTime, ref TimeSpan? sessionTime)
        {
            var clock = ResumeSession.State?.Clock;
            if (!ResumeSession.Loading || clock == null || clock.SessionSeconds <= 0 || Singleton<AbstractGame>.Instance?.GameTimer != __instance)
            {
                return;
            }
            var past = Math.Max(0d, Math.Min(clock.PastSeconds, clock.SessionSeconds));
            startTime = DateTimeExtensions.UtcNow - TimeSpan.FromSeconds(past);
            sessionTime = TimeSpan.FromSeconds(clock.SessionSeconds);
        }
    }

    /// <summary>
    /// B1 restores no bots, so none may spawn: with NoBots, BotsController.Init sets _canSpawn false and every wave and
    /// boss activation is ignored.
    /// </summary>
    internal sealed class BotSettingsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LocalGame), "vmethod_1");

        [PatchPrefix]
        private static void Prefix(ref BotControllerSettings controllerSettings)
        {
            if (ResumeSession.Loading)
            {
                controllerSettings.BotAmount = EBotAmount.NoBots;
            }
        }
    }

    /// <summary>The saved world replaces the server's freshly generated loot before it is spawned.</summary>
    internal sealed class SpawnLootPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), nameof(BaseLocalGame<EftGamePlayerOwner>.SpawnLoot));

        [PatchPrefix]
        private static void Prefix(LocationSettings.Location location)
        {
            if (ResumeSession.Loading && ResumeSession.Loot != null)
            {
                location.Loot = ResumeSession.Loot;
            }
        }
    }

    /// <summary>GameWorld.SpawnLoot(initial: true) marks every item found in raid; the saved flags go back afterwards.</summary>
    internal sealed class WorldSpawnLootPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(GameWorld), nameof(GameWorld.SpawnLoot));

        [PatchPostfix]
        private static void Postfix(bool initial)
        {
            if (ResumeSession.Loading && initial)
            {
                ResumeSession.RestoreFoundInRaid();
            }
        }
    }

    /// <summary>BaseLocalGame.vmethod_3 clears found-in-raid on the player's items at spawn; not on a resumed profile.</summary>
    internal sealed class SpawnedInSessionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Profile), nameof(Profile.SetSpawnedInSession));

        [PatchPrefix]
        private static bool Prefix(Profile __instance, bool value)
        {
            return value || !ResumeSession.Active || !ReferenceEquals(__instance, ResumeSession.Profile);
        }
    }

    /// <summary>The Labs keycard was used when the raid first started; don't use it up again.</summary>
    internal sealed class KeycardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), nameof(BaseLocalGame<EftGamePlayerOwner>.RemoveUsedLocationKeycard));

        [PatchPrefix]
        private static bool Prefix() => !ResumeSession.Active;
    }

    /// <summary>
    /// The game starts (BaseLocalGame.vmethod_6): the timer starts, skill fatigue is zeroed, and the entry point is
    /// overwritten from the spawn point. Keep the saved entry point and fatigue, then restore the rest of the raid.
    /// </summary>
    internal sealed class GameStartPatch : ModulePatch
    {
        private static readonly AccessTools.FieldRef<BaseLocalGame<EftGamePlayerOwner>, string> EntryPoint =
            AccessTools.FieldRefAccess<BaseLocalGame<EftGamePlayerOwner>, string>("_entryPoint");

        private static Dictionary<Skill, float> _fatigue;

        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "vmethod_6");

        [PatchPrefix]
        private static void Prefix(BaseLocalGame<EftGamePlayerOwner> __instance)
        {
            if (!ResumeSession.Loading)
            {
                return;
            }
            var saved = ResumeSession.Profile.Info.EntryPoint;
            if (!string.IsNullOrEmpty(saved))
            {
                EntryPoint(__instance) = saved;
            }
            _fatigue = __instance.Profile.Skills.Skills.ToDictionary(skill => skill, skill => skill.PointsEarned);
        }

        [PatchPostfix]
        private static void Postfix(BaseLocalGame<EftGamePlayerOwner> __instance)
        {
            if (!ResumeSession.Loading)
            {
                return;
            }
            if (_fatigue != null)
            {
                foreach (var pair in _fatigue)
                {
                    pair.Key.SetPointsEarnedInSession(pair.Value);
                }
                _fatigue = null;
            }
            try
            {
                RaidRestore.Apply(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Restoring the raid failed partway: " + e);
            }
            ReadyController.Attach(__instance.GameWorld);
        }
    }
}
