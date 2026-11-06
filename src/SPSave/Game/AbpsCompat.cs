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
        private static FieldInfo _spawnedPerPlayer;

        private static void Look()
        {
            if (_looked)
            {
                return;
            }
            _looked = true;
            if (!Chainloader.PluginInfos.ContainsKey(Guid))
            {
                return;
            }
            var utility = AccessTools.TypeByName("BotPlacementSystemClient.Utils.Utility");
            _cachedPmcs = utility != null ? AccessTools.Field(utility, "CachedPmcs") : null;
            _lock = utility != null ? AccessTools.Field(utility, "SpawnPointLock") : null;
            _spawnedPerPlayer = utility != null ? AccessTools.Field(utility, "BotsSpawnedPerPlayer") : null;
            if (_cachedPmcs == null || _lock == null || _spawnedPerPlayer == null)
            {
                Plugin.Log.LogWarning("ABPS is loaded but its Utility fields were not all found; restored PMCs or its scav cap may be off.");
            }
        }

        /// <summary>
        /// Utility.BotsSpawnedPerPlayer: how many trickle scavs ABPS has spawned per player, capped by the map's
        /// NonWaveSpawnBotsLimitPerPlayerPvE. It restarts at 0 on BotsController.Init, so a resume sets it back.
        /// </summary>
        internal static double? ReadSpawnedPerPlayer()
        {
            Look();
            try
            {
                return _spawnedPerPlayer?.GetValue(null) as double?;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string WriteSpawnedPerPlayer(double? value)
        {
            Look();
            if (value == null || _spawnedPerPlayer == null)
            {
                return null;
            }
            try
            {
                _spawnedPerPlayer.SetValue(null, value.Value);
                return $"ABPS scav count {value.Value:F1}/player";
            }
            catch (Exception e)
            {
                return "ABPS scav count NOT restored: " + e.Message;
            }
        }

        internal static bool AddPmc(Player player)
        {
            Look();
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
