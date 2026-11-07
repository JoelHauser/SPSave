using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SPSave.Server
{
    /// <summary>
    /// Daily and weekly quests don't rotate while a saved raid waits. The menu asks for them on every load
    /// (client/repeatalbeQuests/activityPeriods), and an expired set would be rotated: the old quests' statuses taken out
    /// of pmcData.Quests and a new set generated. The resumed raid's end then puts the saved quest list back
    /// (local/end assigns pmcData.Quests from the client profile), old dailies included, beside a set the player never
    /// saw. While locked, every set the profile has counts as still valid; the first menu after the raid ends rotates
    /// them as usual, against the quest statuses the raid ended with (an AvailableForFinish daily is kept, as always).
    /// </summary>
    internal sealed class RepeatableQuestsFreezePatch : AbstractPatch
    {
        internal static ProfileHelper Profiles;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(RepeatableQuestController), nameof(RepeatableQuestController.GetClientRepeatableQuests));

        /// <summary>Pushes each set's EndTime past the expiry check; the postfix puts the real ones back before the reply is written.</summary>
        [PatchPrefix]
        private static void Prefix(MongoId sessionID, out List<KeyValuePair<PmcDataRepeatableQuest, long?>> __state)
        {
            __state = null;
            if (!LockStore.IsLocked(sessionID.ToString()))
            {
                return;
            }
            var sets = Profiles?.GetPmcProfile(sessionID)?.RepeatableQuests;
            if (sets == null)
            {
                return;
            }
            __state = new List<KeyValuePair<PmcDataRepeatableQuest, long?>>();
            foreach (var set in sets)
            {
                if (set == null)
                {
                    continue;
                }
                __state.Add(new KeyValuePair<PmcDataRepeatableQuest, long?>(set, set.EndTime));
                set.EndTime = long.MaxValue;
            }
        }

        [PatchPostfix]
        private static void Postfix(MongoId sessionID, List<KeyValuePair<PmcDataRepeatableQuest, long?>> __state)
        {
            if (__state == null)
            {
                return;
            }
            var frozen = 0;
            foreach (var pair in __state)
            {
                pair.Key.EndTime = pair.Value;
                if (pair.Value.HasValue && pair.Value.Value <= System.DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    frozen++;
                }
            }
            if (frozen > 0)
            {
                SPSaveLoader.Log($"Kept {frozen} expired repeatable quest set(s) for {sessionID} unrotated: a saved raid is waiting.");
            }
        }
    }
}
