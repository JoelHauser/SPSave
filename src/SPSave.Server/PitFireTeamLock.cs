using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;

namespace SPSave.Server
{
    /// <summary>
    /// Pit FireTeam (optional) keeps its teammates' gear on the server. While a save waits, a change there would be
    /// undone or duplicated by the resume: a deleted teammate's kit is mailed back while the saved raid still has it.
    /// So its gear and roster changes are refused like any other locked action (docs/PITFIRETEAM-COMPAT.md). Found by
    /// name, so SPSave doesn't need Pit FireTeam; cosmetic changes (name, voice, suit, tactic) stay allowed.
    /// </summary>
    internal static class PitFireTeamLock
    {
        private const string CallbacksType = "pitTeam.Server.Callbacks.FriendlyTeammateCallbacks";

        private static readonly string[] Locked =
        {
            "Create", "PrepareCreation", "Delete", "SetLoadout", "SaveDefaultEquipment", "BuyKit",
            "RepairDefaultEquipment", "InsureEquipment", "Treatment"
        };

        internal static string Enable()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(CallbacksType, throwOnError: false))
                .FirstOrDefault(t => t != null);
            if (type == null)
            {
                return null;
            }
            var harmony = new Harmony("com.joelhauser.spsave.server.pitfireteam");
            var prefix = new HarmonyMethod(typeof(PitFireTeamLock), nameof(Prefix));
            var patched = new List<string>();
            var missing = new List<string>();
            foreach (var name in Locked)
            {
                var method = AccessTools.Method(type, name);
                if (method == null || method.ReturnType != typeof(ValueTask<string>) || !method.GetParameters().Any(p => p.ParameterType == typeof(MongoId)))
                {
                    missing.Add(name);
                    continue;
                }
                harmony.Patch(method, prefix: prefix);
                patched.Add(name);
            }
            return $"Pit FireTeam found: {patched.Count} teammate actions locked while a save waits" +
                   (missing.Count > 0 ? $" (NOT found: {string.Join(", ", missing)})" : "");
        }

        private static bool Prefix(MethodBase __originalMethod, object[] __args, ref ValueTask<string> __result)
        {
            var session = __args.OfType<MongoId>().FirstOrDefault();
            return Refusal.Refuse(session, "Pit FireTeam " + __originalMethod.Name, ref __result);
        }
    }
}
