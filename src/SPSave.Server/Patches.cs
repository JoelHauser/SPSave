using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Launcher;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;

namespace SPSave.Server
{
    // The lock (docs/C3-PLAN.md, C-SERVER-API.md). A mod router can't refuse a request (every matching router runs,
    // the core's first), so these are prefixes on the core's callbacks. The client's menu is the main guard; these
    // stop anything that gets past it (other mods, a client without SPSave).

    internal static class Refusal
    {
        internal const string Message = "SPSave: this profile has a saved raid waiting. Resume or abandon it first.";

        internal static bool Refuse(MongoId sessionID, string what, ref ValueTask<string> result)
        {
            if (!LockStore.IsLocked(sessionID.ToString()))
            {
                return true;
            }
            SPSaveLoader.Log($"Refused {what} for {sessionID}: a saved raid is waiting.");
            result = new ValueTask<string>(SPSaveLoader.ErrorBody(BackendErrorCodes.PlayerInRaid, Message));
            return false;
        }
    }

    /// <summary>Every item event: inventory moves, trades, flea, hideout, repair, insurance, quests, health, notes.</summary>
    internal sealed class ItemEventsLockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemEventCallbacks), nameof(ItemEventCallbacks.HandleEvents));

        [PatchPrefix]
        private static bool Prefix(MongoId sessionID, ref ValueTask<string> __result) =>
            Refusal.Refuse(sessionID, "an item event", ref __result);
    }

    /// <summary>A raid start is allowed only for the saved raid's map and side: that is the resume itself.</summary>
    internal sealed class RaidStartLockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(MatchCallbacks), nameof(MatchCallbacks.StartLocalRaidAsync));

        [PatchPrefix]
        private static bool Prefix(StartLocalRaidRequestData info, MongoId sessionID, ref ValueTask<string> __result)
        {
            var raidLock = LockStore.Get(sessionID.ToString());
            var side = raidLock == null ? null : RaidSide(raidLock);
            if (raidLock == null ||
                (string.Equals(info?.Location, raidLock.Map, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(info?.PlayerSide, side, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            SPSaveLoader.Log($"Refused a raid on {info?.Location} ({info?.PlayerSide}) for {sessionID}: the saved raid is on {raidLock.Map} ({side}).");
            __result = new ValueTask<string>(SPSaveLoader.ErrorBody(BackendErrorCodes.PlayerInRaid, Refusal.Message));
            return false;
        }

        /// <summary>
        /// The raid side ("Pmc"/"Savage") as local/start sends it. Taken from the saved raid's serverId, which the server
        /// built from that same request ("&lt;location&gt;.&lt;side&gt; &lt;stamp&gt;"; EndLocalRaidAsync parses it the same way).
        /// Not the lock's Side: 0.7.0 clients sent the character's side ("Usec"), and refused their own resume.
        /// </summary>
        private static string RaidSide(RaidLock raidLock)
        {
            var parts = raidLock.ServerId?.Split(' ')[0].Split('.');
            return parts != null && parts.Length >= 2 ? parts[1] : raidLock.Side;
        }
    }

    /// <summary>
    /// Backstop for the client's unlock: when a raid ends for a locked profile, its save was used up on Ready or
    /// ended as Killed/MIA (the client unlocks first; a raid can't otherwise start while locked).
    /// </summary>
    internal sealed class RaidEndUnlockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(MatchCallbacks), nameof(MatchCallbacks.EndLocalRaidAsync));

        [PatchPrefix]
        private static void Prefix(MongoId sessionID)
        {
            if (LockStore.Clear(sessionID.ToString()))
            {
                SPSaveLoader.Log($"Profile {sessionID} unlocked at raid end (the client hadn't).");
            }
        }
    }

    internal sealed class ScavSaveLockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(InraidCallbacks), nameof(InraidCallbacks.SaveProgress));

        [PatchPrefix]
        private static bool Prefix(MongoId sessionID, ref ValueTask<string> __result) =>
            Refusal.Refuse(sessionID, "a scav save", ref __result);
    }

    internal sealed class PrestigeLockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(PrestigeCallbacks), nameof(PrestigeCallbacks.ObtainPrestige));

        [PatchPrefix]
        private static bool Prefix(MongoId sessionID, ref ValueTask<string> __result) =>
            Refusal.Refuse(sessionID, "prestige", ref __result);
    }

    internal sealed class ScavRegenerateLockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ProfileCallbacks), nameof(ProfileCallbacks.RegenerateScav));

        [PatchPrefix]
        private static bool Prefix(MongoId sessionID, ref ValueTask<string> __result) =>
            Refusal.Refuse(sessionID, "scav regeneration", ref __result);
    }

    /// <summary>A wiped or removed profile has nothing to resume into: drop its lock (the client then discards the save).</summary>
    internal sealed class WipeUnlockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LauncherV2Controller), nameof(LauncherV2Controller.Wipe));

        [PatchPrefix]
        private static void Prefix(LauncherV2Controller __instance, RegisterData info) => LauncherUnlock.Drop(__instance, info, "wiped");
    }

    internal sealed class RemoveUnlockPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LauncherV2Controller), nameof(LauncherV2Controller.Remove));

        [PatchPrefix]
        private static void Prefix(LauncherV2Controller __instance, LoginRequestData info) => LauncherUnlock.Drop(__instance, info, "removed");
    }

    internal static class LauncherUnlock
    {
        private static readonly MethodInfo GetSessionId = AccessTools.Method(typeof(LauncherV2Controller), "GetSessionId");

        internal static void Drop(LauncherV2Controller controller, LoginRequestData info, string what)
        {
            try
            {
                var id = GetSessionId?.Invoke(controller, new object[] { info });
                if (id != null && LockStore.Clear(id.ToString()))
                {
                    SPSaveLoader.Log($"Profile {id} {what} from the launcher: its lock is dropped.");
                }
            }
            catch (Exception e)
            {
                SPSaveLoader.Log($"Dropping the lock of a {what} profile failed: {e.Message}");
            }
        }
    }
}
