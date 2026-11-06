using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.BinarySerialization;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Weather;
using HarmonyLib;
using JsonType;
using Mirror;
using Newtonsoft.Json;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    internal sealed class CaptureResult
    {
        public readonly List<KeyValuePair<string, byte[]>> Sections = new List<KeyValuePair<string, byte[]>>();
        public readonly CaptureReport Report = new CaptureReport();
        public readonly HashSet<string> WorldItemIds = new HashSet<string>(StringComparer.Ordinal);
        public string ProfileId;
    }

    /// <summary>
    /// Reads one raid into save sections. Read-only: nothing here changes the game, so a failed capture leaves the
    /// raid exactly as it was. Runs in a single frame, so the result is one consistent moment without freezing.
    /// Engine-owned data goes through the game's own serializers: ProfileDescriptor (as at raid end, see
    /// BaseLocalGame.GameEnd) and the late-join world format (ItemBinarySerializer.SerializeLootData).
    /// </summary>
    internal static class RaidCapture
    {
        public const string ManifestSection = "manifest.json";
        public const string StateSection = "state.json";
        public const string PlayerSection = "player.profile";
        public const string BotSectionPrefix = "bot/";
        public const string LootSection = "world.loot";
        public const string WeatherSection = "weather.json";

        private static readonly AccessTools.FieldRef<BaseLocalGame<EftGamePlayerOwner>, LocalRaidSettings> RaidSettings =
            AccessTools.FieldRefAccess<BaseLocalGame<EftGamePlayerOwner>, LocalRaidSettings>("_raidSettings");

        public static CaptureResult Run(float combatWindowSeconds)
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world != null ? world.MainPlayer : null;
            if (player == null)
            {
                throw new InvalidOperationException("No raid in progress.");
            }

            var result = new CaptureResult { ProfileId = player.ProfileId };
            var report = result.Report;
            report.Utc = DateTime.UtcNow;
            report.Map = world.LocationId ?? "";
            report.ProfileId = player.ProfileId;
            var state = new RaidState();
            var watch = Stopwatch.StartNew();

            // Player: profile as GameEnd would send it, with live health instead of the pre-raid copy.
            var playerDescriptor = Describe(player);
            result.Sections.Add(Section(PlayerSection, Binary(w => w.WriteEFTProfileDescriptor(playerDescriptor))));
            state.Player = ReadPlayer(player);
            report.PlayerEffects = state.Player.Effects.Count;
            report.PlayerItemIds = player.Profile.Inventory.GetPlayerItems(EPlayerItems.All).Select(i => i.Id).Distinct().Count();
            report.PlayerMs = Lap(watch);

            // Living bots: their profiles (identity, gear, health), where they are, and what they know about the player.
            var awareness = new List<BotAwareness>();
            foreach (var bot in world.AllAlivePlayersList)
            {
                if (bot == null || bot.IsYourPlayer)
                {
                    continue;
                }
                try
                {
                    var descriptor = Describe(bot);
                    var bytes = Binary(w => w.WriteEFTProfileDescriptor(descriptor));
                    var botState = ReadBot(bot, player, out var known);
                    result.Sections.Add(Section(BotSectionPrefix + bot.ProfileId, bytes));
                    state.Bots.Add(botState);
                    awareness.Add(known);
                }
                catch (Exception e)
                {
                    report.SkippedBots++;
                    Plugin.Log.LogWarning($"Bot {bot.ProfileId} not captured: {e}");
                }
            }
            report.BotsAlive = state.Bots.Count;
            var verdict = CombatRule.Evaluate(awareness, combatWindowSeconds);
            state.Combat = new CombatState { WindowSeconds = combatWindowSeconds, InCombat = verdict.InCombat, Detail = verdict.Describe() };
            report.InCombat = verdict.InCombat;
            report.CombatDetail = verdict.Describe();
            report.BotsMs = Lap(watch);

            // World loot: loose items, containers with their current contents, corpses with their pose.
            var loot = CollectLoot(world, report, result.WorldItemIds);
            var lootDescriptor = ItemBinarySerializer.SerializeLootData(loot, FullySearchedSearchController.Instance);
            result.Sections.Add(Section(LootSection, Binary(w => w.WriteEFTLootDataDescriptor(lootDescriptor))));
            report.WorldItemIds = result.WorldItemIds.Count;
            report.LootMs = Lap(watch);

            // Doors, raid clock, time of day, weather.
            state.Doors = ReadDoors();
            report.Doors = state.Doors.Count;
            var nodes = WeatherNodesPatch.Last;
            state.Clock = ReadClock(world, nodes);
            report.Weather = !state.Clock.HasWeather ? "none on this map"
                : state.Clock.WeatherNodes == 0 ? "NODES MISSING"
                : $"{state.Clock.WeatherNodes} nodes{(state.Clock.WeatherMatchesCurve ? "" : ", curve differs")}";
            if (nodes != null)
            {
                result.Sections.Add(Section(WeatherSection, Encoding.UTF8.GetBytes(nodes.ToJson())));
            }
            report.WorldMs = Lap(watch);

            result.Sections.Insert(0, Section(ManifestSection, Json(BuildManifest(world, player))));
            result.Sections.Add(Section(StateSection, Json(state)));
            report.StateMs = Lap(watch);
            return result;
        }

        internal static ProfileDescriptor Describe(Player player)
        {
            var descriptor = new ProfileDescriptor(player.Profile, FullySearchedSearchController.Instance);
            var health = player.ActiveHealthController;
            if (health != null)
            {
                descriptor.Health = health.Store();
            }
            return descriptor;
        }

        internal static byte[] Binary(Action<NetworkWriter> write)
        {
            var writer = new NetworkWriter();
            write(writer);
            return writer.ToArray();
        }

        internal static void AddIds(Item root, HashSet<string> ids)
        {
            if (root == null)
            {
                return;
            }
            ids.Add(root.Id);
            if (root is ContainerCollection collection)
            {
                foreach (var item in collection.GetAllItemsFromCollection())
                {
                    ids.Add(item.Id);
                }
            }
        }

        private static List<JsonLootItem> CollectLoot(GameWorld world, CaptureReport report, HashSet<string> ids)
        {
            var loot = new List<JsonLootItem>(world.LootList.Count);
            var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Skip(string reason)
            {
                report.SkippedLoot++;
                skipped[reason] = skipped.TryGetValue(reason, out var n) ? n + 1 : 1;
            }

            foreach (var killable in world.LootList)
            {
                try
                {
                    switch (killable)
                    {
                        case Corpse corpse:
                        {
                            var root = corpse.ItemOwner?.RootItem;
                            if (root == null || corpse.Customization == null)
                            {
                                Skip(root == null ? "Corpse without equipment" : "Corpse without customization");
                                break;
                            }
                            var t = corpse.transform;
                            loot.Add(new JsonCorpse
                            {
                                Id = corpse.ItemId ?? root.Id,
                                Item = root,
                                Position = t.position,
                                Rotation = t.rotation.eulerAngles,
                                Customization = corpse.Customization,
                                Side = corpse.Side,
                                // Fresh, not the cached TransformSyncs: a ragdoll can settle after the cache is filled.
                                Bones = corpse.GetTransformSync(),
                                ProfileID = corpse.PlayerProfileID,
                                IsZombieCorpse = corpse.IsZombieCorpse,
                                PlatformId = -1
                            });
                            report.Corpses++;
                            AddIds(root, ids);
                            break;
                        }
                        case LootItem item:
                        {
                            var root = item.ItemOwner?.RootItem ?? item.Item;
                            if (root == null)
                            {
                                Skip(item.GetType().Name + " without item");
                                break;
                            }
                            var t = item.transform;
                            loot.Add(new JsonLootItem
                            {
                                Id = item.ItemId ?? root.Id,
                                Item = root,
                                Position = t.position,
                                Rotation = t.rotation.eulerAngles,
                                PlatformId = -1
                            });
                            report.LooseItems++;
                            AddIds(root, ids);
                            break;
                        }
                        case LootableContainer container:
                        {
                            var root = container.ItemOwner?.RootItem;
                            if (root == null || string.IsNullOrEmpty(container.Id))
                            {
                                Skip(root == null ? container.GetType().Name + " without items" : container.GetType().Name + " without id");
                                break;
                            }
                            var t = container.transform;
                            loot.Add(new JsonLootItem
                            {
                                Id = container.Id,
                                Item = root,
                                IsContainer = true,
                                Position = t.position,
                                Rotation = t.rotation.eulerAngles,
                                PlatformId = -1
                            });
                            report.Containers++;
                            AddIds(root, ids);
                            break;
                        }
                        default:
                            Skip(killable == null ? "null entry" : killable.GetType().Name);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Skip($"{killable?.GetType().Name}: {e.GetType().Name}");
                    Plugin.Log.LogWarning($"Loot entry {killable?.GetType().Name} not captured: {e}");
                }
            }
            report.SkippedLootKinds = string.Join("; ", skipped.Select(k => $"{k.Key} x{k.Value}"));
            if (skipped.Count > 0)
            {
                Plugin.Log.LogInfo("Loot entries not captured: " + report.SkippedLootKinds);
            }
            return loot;
        }

        private static PlayerState ReadPlayer(Player player)
        {
            var state = new PlayerState
            {
                ProfileId = player.ProfileId,
                Position = Floats(player.Position),
                Rotation = Floats(player.Rotation),
                PoseLevel = player.PoseLevel,
                Prone = player.IsInPronePose,
                Effects = ReadEffects(player.ActiveHealthController)
            };
            var physical = player.Physical;
            if (physical != null)
            {
                state.Stamina = physical.Stamina?.Current ?? 0f;
                state.HandsStamina = physical.HandsStamina?.Current ?? 0f;
                state.Oxygen = physical.Oxygen?.Current ?? 0f;
            }
            var hands = player.HandsController;
            if (hands != null && hands.Item != null)
            {
                state.HandsItemId = hands.Item.Id;
                state.HandsItemTemplate = hands.Item.TemplateId.ToString();
            }
            return state;
        }

        private static BotState ReadBot(Player bot, Player mainPlayer, out BotAwareness awareness)
        {
            var state = new BotState
            {
                ProfileId = bot.ProfileId,
                Role = bot.Profile?.Info?.Settings?.Role.ToString(),
                Side = bot.Side.ToString(),
                Position = Floats(bot.Position),
                Rotation = Floats(bot.Rotation),
                PoseLevel = bot.PoseLevel,
                Prone = bot.IsInPronePose,
                Effects = ReadEffects(bot.ActiveHealthController)
            };
            var hands = bot.HandsController;
            if (hands != null && hands.Item != null)
            {
                state.HandsItemId = hands.Item.Id;
            }

            awareness = default;
            var owner = bot.AIData?.BotOwner;
            var enemies = owner != null ? owner.EnemiesController?.EnemyInfos : null;
            if (enemies != null && enemies.TryGetValue(mainPlayer, out var info) && info != null)
            {
                var now = Time.time;
                awareness.Knows = true;
                awareness.Visible = info.IsVisible;
                // PersonalSeenTime is set the first time the bot sees the player; before that the last-seen time is meaningless.
                awareness.SecondsSinceSeen = info.PersonalSeenTime > 0f ? now - info.PersonalLastSeenTime : (float?)null;
                awareness.SecondsSinceShot = info.PersonalLastShootTime >= 0f ? now - info.PersonalLastShootTime : (float?)null;
            }
            state.KnowsPlayer = awareness.Knows;
            state.SeesPlayer = awareness.Visible;
            state.SecondsSinceSeenPlayer = awareness.SecondsSinceSeen;
            state.SecondsSinceShotAtPlayer = awareness.SecondsSinceShot;
            return state;
        }

        private static List<EffectState> ReadEffects(ActiveHealthController health)
        {
            var effects = new List<EffectState>();
            if (health == null)
            {
                return effects;
            }
            foreach (var effect in health.Effects)
            {
                effects.Add(new EffectState
                {
                    Type = effect.GetType().Name,
                    BodyPart = effect.BodyPart.ToString(),
                    State = effect.State.ToString(),
                    StateCurrentTime = effect.StateCurrentTime,
                    TimeLeft = effect.TimeLeft,
                    DelayTime = effect.DelayTime,
                    BuildUpTime = effect.BuildUpTime,
                    WorkStateTime = effect.WorkStateTime,
                    ResidueStateTime = effect.ResidueStateTime,
                    Strength = effect.Strength
                });
            }
            return effects;
        }

        private static List<DoorState> ReadDoors()
        {
            var doors = new List<DoorState>();
            foreach (var obj in LocationScene.GetAllObjects<WorldInteractiveObject>())
            {
                if (obj == null || string.IsNullOrEmpty(obj.Id))
                {
                    continue;
                }
                doors.Add(new DoorState { Id = obj.Id, Kind = obj.GetType().Name, State = (int)obj.DoorState });
            }
            return doors;
        }

        private static ClockState ReadClock(GameWorld world, WeatherNode[] nodes)
        {
            var clock = new ClockState();
            var timer = Singleton<AbstractGame>.Instance?.GameTimer;
            if (timer != null)
            {
                clock.PastSeconds = timer.PastTime.TotalSeconds;
                clock.SessionSeconds = timer.SessionTime?.TotalSeconds ?? 0d;
                clock.RemainingSeconds = clock.SessionSeconds - clock.PastSeconds;
            }
            var gameTime = world.GameDateTime;
            if (gameTime != null)
            {
                var now = gameTime.Calculate();
                clock.GameTimeTicks = now.Ticks;
                clock.GameTime = now.ToString("yyyy-MM-dd HH:mm:ss");
                clock.TimeFactor = gameTime.TimeFactor;
                clock.TimeFactorMod = gameTime.TimeFactorMod;
            }
            clock.HasWeather = WeatherController.Instance != null;
            clock.WeatherNodes = nodes?.Length ?? 0;
            var curve = clock.HasWeather ? WeatherController.Instance.WeatherCurve as WeatherCurve : null;
            clock.WeatherMatchesCurve = nodes != null && nodes.Length > 0 && curve != null && curve.StartTime == nodes[0].Time;
            return clock;
        }

        private static Manifest BuildManifest(GameWorld world, Player player)
        {
            var manifest = new Manifest
            {
                ModVersion = Plugin.Version,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                GameVersion = GameVersion(),
                SptVersion = PluginVersion("com.SPT.core"),
                ProfileId = player.ProfileId,
                Side = player.Side.ToString(),
                Map = world.LocationId
            };
            if (Singleton<AbstractGame>.Instance is BaseLocalGame<EftGamePlayerOwner> game)
            {
                var settings = RaidSettings(game);
                if (settings != null)
                {
                    manifest.ServerId = settings.serverId;
                    manifest.TimeVariant = settings.timeVariant.ToString();
                }
            }
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(i => i.Metadata.GUID, StringComparer.Ordinal))
            {
                manifest.Plugins.Add(info.Metadata.GUID + "@" + info.Metadata.Version);
            }
            return manifest;
        }

        /// <summary>
        /// The game's build id, e.g. "0.16.9.5.40743". It is a const (BuildInfo.string_0), so it is read by reflection:
        /// referencing it would bake in the value from build time. Mono reads the exe's file version truncated
        /// ("0.16.9.4074" in the first in-game run), so that is only a fallback.
        /// </summary>
        private static string GameVersion()
        {
            try
            {
                if (AccessTools.Field(typeof(BuildInfo), "string_0")?.GetRawConstantValue() is string build && build.Length > 0)
                {
                    return build;
                }
                var file = FileVersionInfo.GetVersionInfo(BepInEx.Paths.ExecutablePath);
                return $"{file.FileMajorPart}.{file.FileMinorPart}.{file.FileBuildPart}.{file.FilePrivatePart}";
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string PluginVersion(string guid)
        {
            return Chainloader.PluginInfos.TryGetValue(guid, out var info) ? info.Metadata.Version.ToString() : null;
        }

        private static KeyValuePair<string, byte[]> Section(string name, byte[] data)
        {
            return new KeyValuePair<string, byte[]>(name, data);
        }

        private static byte[] Json(object value)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        private static float[] Floats(Vector3 v)
        {
            return new[] { v.x, v.y, v.z };
        }

        private static float[] Floats(Vector2 v)
        {
            return new[] { v.x, v.y };
        }

        private static double Lap(Stopwatch watch)
        {
            var ms = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            return ms;
        }
    }
}
