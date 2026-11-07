using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Weather;
using HarmonyLib;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Puts the saved raid back once the game has started and the player exists: position, pose, stamina, health
    /// effects that the profile can't carry, doors, weather and time of day. The profile, loot and timer were
    /// substituted earlier by ResumePatches.
    /// </summary>
    internal static class RaidRestore
    {
        private static readonly AccessTools.FieldRef<WeatherController, IWeatherCurve> BackendWeatherCurve =
            AccessTools.FieldRefAccess<WeatherController, IWeatherCurve>("backendWeatherCurve");

        internal static void Apply(BaseLocalGame<EftGamePlayerOwner> game)
        {
            var state = ResumeSession.State;
            var world = Singleton<GameWorld>.Instance;
            var player = world.MainPlayer;
            var notes = new List<string>();

            Run("player", notes, () => Place(player, state.Player));
            Run("effects", notes, () => notes.Add(RestoreEffects(player.ActiveHealthController, state.Player.Effects)));
            Run("doors", notes, () => notes.Add(RestoreDoors(state.Doors)));
            Run("search", notes, () => notes.Add(RestoreSearch(player, state.Player)));
            Run("weather", notes, () => notes.Add(RestoreWeather()));
            Run("time of day", notes, () => notes.Add(RestoreTimeOfDay(world, state.Clock)));
            // A save being ended (Killed/MIA) ends a frame after the start: its bots would never be seen, and SAIN,
            // which takes restored bots on only after Ready, throws in BotComponent.OnDisable for each one when the
            // raid unloads (0.7.2 Abandon test: 9 bots, 18 errors).
            if (ResumeSession.Outcome == null)
            {
                Run("bots", notes, () => notes.Add(BotRestore.Spawn(game as IBotGame, state)));
            }
            else
            {
                notes.Add($"bots not brought back: the raid ends as {ResumeSession.Outcome}");
            }
            // ABPS reset its count in its BotsController.Init postfix, which ran before the game started.
            Run("ABPS", notes, () => notes.Add(AbpsCompat.WriteSpawnedPerPlayer(state.Waves?.AbpsBotsSpawnedPerPlayer)));

            Plugin.Log.LogInfo("Raid restored: " + string.Join("; ", notes.Where(n => !string.IsNullOrEmpty(n))));
        }

        internal static string RestoreTimeOfDay(GameWorld world, ClockState clock)
        {
            if (clock == null || clock.GameTimeTicks <= 0 || world.GameDateTime == null)
            {
                return "time of day not saved";
            }
            world.GameDateTime.ResetForce(new DateTime(clock.GameTimeTicks, DateTimeKind.Utc));
            return "time of day " + clock.GameTime;
        }

        private static void Place(Player player, PlayerState saved)
        {
            if (saved.Position != null && saved.Position.Length == 3)
            {
                player.Teleport(new Vector3(saved.Position[0], saved.Position[1], saved.Position[2]));
            }
            if (saved.Rotation != null && saved.Rotation.Length == 2)
            {
                player.Rotation = new Vector2(saved.Rotation[0], saved.Rotation[1]);
            }
            player.MovementContext.SetPoseLevel(saved.PoseLevel, force: true);
            var physical = player.Physical;
            if (physical != null)
            {
                if (physical.Stamina != null) physical.Stamina.Current = saved.Stamina;
                if (physical.HandsStamina != null) physical.HandsStamina.Current = saved.HandsStamina;
                if (physical.Oxygen != null) physical.Oxygen.Current = saved.Oxygen;
            }
        }

        /// <summary>
        /// Bleeds, fractures and intoxication are IRestorable and come back with the profile's health. These are the
        /// ones the profile drops; anything else that was active (stun, flash, pain from a fracture) is left to the game.
        /// </summary>
        internal static string RestoreEffects(ActiveHealthController health, List<EffectState> saved)
        {
            if (health == null || saved == null)
            {
                return null;
            }
            int added = 0, skipped = 0;
            foreach (var effect in saved)
            {
                if (!Enum.TryParse<EBodyPart>(effect.BodyPart, out var part))
                {
                    skipped++;
                    continue;
                }
                var times = EffectTiming.For(effect.State, effect.TimeLeft, effect.WorkStateTime);
                if (!times.Restore)
                {
                    continue;
                }
                bool done;
                switch (effect.Type)
                {
                    case nameof(ActiveHealthController.Contusion):
                        done = Add<ActiveHealthController.Contusion>(health, part, times, effect, null);
                        break;
                    case nameof(ActiveHealthController.Tremor):
                        done = Add<ActiveHealthController.Tremor>(health, part, times, effect, null);
                        break;
                    case nameof(ActiveHealthController.TunnelVision):
                        done = Add<ActiveHealthController.TunnelVision>(health, part, times, effect, null);
                        break;
                    case nameof(ActiveHealthController.PainKiller):
                        var maxDuration = effect.MaxDuration > 0f ? effect.MaxDuration : times.Work ?? 0f;
                        done = Add<ActiveHealthController.PainKiller>(health, part, times, effect,
                            painKiller => painKiller.StoreValues(effect.ItemTemplateId, maxDuration));
                        break;
                    default:
                        continue;
                }
                if (done) added++; else skipped++;
            }
            return $"{added} effects re-added" + (skipped > 0 ? $", {skipped} skipped" : "");
        }

        private static bool Add<T>(ActiveHealthController health, EBodyPart part, EffectTimes times, EffectState saved, Action<T> init)
            where T : ActiveHealthController.Effect, new()
        {
            if (health.Effects.Any(e => e is T && e.BodyPart == part))
            {
                return false;
            }
            health.AddEffect(part, times.Delay, times.Work, saved.ResidueStateTime > 0f ? saved.ResidueStateTime : (float?)null, saved.Strength, init);
            return true;
        }

        /// <summary>
        /// Marks the world's containers searched and their items known as they were at the save, quietly:
        /// UncoverItem and DiscoverItem raise no "searched"/"found" events, so quest counters don't fire again.
        /// </summary>
        private static string RestoreSearch(Player player, PlayerState saved)
        {
            var search = player.SearchController as PlayerSearchController;
            var active = player.SearchController as ActiveSearchController;
            if (search == null || ResumeSession.Loot == null)
            {
                return "search state not restored (no search controller)";
            }
            var world = new Dictionary<string, Item>(StringComparer.Ordinal);
            foreach (var entry in ResumeSession.Loot)
            {
                var items = entry.Item is ContainerCollection collection ? collection.GetAllItemsFromCollection() : new[] { entry.Item };
                foreach (var item in items)
                {
                    if (item != null)
                    {
                        world[item.Id] = item;
                    }
                }
            }
            int searched = 0, known = 0;
            foreach (var id in saved.Searched ?? new List<string>())
            {
                if (world.TryGetValue(id, out var item) && item is SearchableItem searchable && !search.IsSearched(searchable))
                {
                    search.UncoverItem(searchable);
                    searched++;
                }
            }
            if (active != null)
            {
                foreach (var id in saved.Known ?? new List<string>())
                {
                    if (world.TryGetValue(id, out var item) && item.Parent != null && !active.IsItemKnown(item))
                    {
                        active.DiscoverItem(item);
                        known++;
                    }
                }
            }
            return $"{searched} containers searched, {known} items known";
        }

        /// <summary>WorldInteractiveObject.SetInitialSyncState is the late-join path: state and angle, no interaction events.</summary>
        private static string RestoreDoors(List<DoorState> saved)
        {
            var byId = new Dictionary<string, WorldInteractiveObject>(StringComparer.Ordinal);
            foreach (var obj in LocationScene.GetAllObjects<WorldInteractiveObject>())
            {
                if (obj != null && !string.IsNullOrEmpty(obj.Id) && !byId.ContainsKey(obj.Id))
                {
                    byId.Add(obj.Id, obj);
                }
            }
            int changed = 0, missing = 0;
            const EDoorState settled = EDoorState.Locked | EDoorState.Shut | EDoorState.Open;
            foreach (var door in saved ?? new List<DoorState>())
            {
                if (!byId.TryGetValue(door.Id, out var obj))
                {
                    missing++;
                    continue;
                }
                var state = (EDoorState)door.State;
                if ((state & settled) == 0)
                {
                    continue;
                }
                var breach = door.Broken && obj is Door d && !d.IsBroken;
                if (obj.DoorState == (state & settled) && !breach)
                {
                    continue;
                }
                obj.SetInitialSyncState(new WorldInteractiveObject.InteractiveObjectStatusInfo(door.Id, state, 0f) { IsBroken = door.Broken });
                changed++;
            }
            return $"{changed} doors changed" + (missing > 0 ? $", {missing} not found" : "");
        }

        /// <summary>
        /// Sets the saved curve the way WeatherController.SetWeatherNodes does, without calling it: other mods
        /// (Forecaster) rewrite the nodes passed to SetWeatherNodes.
        /// </summary>
        private static string RestoreWeather()
        {
            var nodes = ResumeSession.Weather;
            var controller = WeatherController.Instance;
            if (nodes == null || nodes.Length == 0 || controller == null)
            {
                return controller == null ? "no weather on this map" : "no weather saved";
            }
            BackendWeatherCurve(controller) = new WeatherCurve(nodes);
            controller.WeatherDebug.Enabled = false;
            controller.WeatherDebug.CopyParams(BackendWeatherCurve(controller));
            WeatherNodesPatch.Last = nodes;
            return $"weather {nodes.Length} nodes";
        }

        private static void Run(string what, List<string> notes, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                notes.Add(what + " FAILED");
                Plugin.Log.LogError($"Restoring {what} failed: {e}");
            }
        }
    }
}
