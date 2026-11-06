using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;

namespace SPSave.Game
{
    /// <summary>
    /// ABPS (com.acidphantasm.botplacementsystem) is optional. It keeps PMCs away from each other on spawn using
    /// Utility.CachedPmcs, which only its own PMC spawner fills; scavs and bosses enter through BotOwner.Create, so
    /// restored ones are already counted. Restored PMCs are added here. See docs/B2-COMPAT.md.
    /// </summary>
    internal static class AbpsCompat
    {
        internal const string Guid = "com.acidphantasm.botplacementsystem";

        private static bool _looked;
        private static FieldInfo _cachedPmcs;
        private static FieldInfo _lock;

        internal static bool AddPmc(Player player)
        {
            if (!_looked)
            {
                _looked = true;
                if (Chainloader.PluginInfos.ContainsKey(Guid))
                {
                    var utility = AccessTools.TypeByName("BotPlacementSystemClient.Utils.Utility");
                    _cachedPmcs = utility != null ? AccessTools.Field(utility, "CachedPmcs") : null;
                    _lock = utility != null ? AccessTools.Field(utility, "SpawnPointLock") : null;
                    if (_cachedPmcs == null || _lock == null)
                    {
                        Plugin.Log.LogWarning("ABPS is loaded but Utility.CachedPmcs was not found; it won't know about restored PMCs.");
                    }
                }
            }
            if (_cachedPmcs == null || _lock == null || player == null)
            {
                return false;
            }
            try
            {
                if (!(_cachedPmcs.GetValue(null) is List<Player> list))
                {
                    return false;
                }
                lock (_lock.GetValue(null))
                {
                    if (!list.Contains(player))
                    {
                        list.Add(player);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Adding a restored PMC to ABPS failed: " + e.Message);
                return false;
            }
        }
    }
}
