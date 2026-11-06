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
        /// <summary>Every live world item by id, with where it sits, for naming items a round trip loses.</summary>
        public readonly Dictionary<string, KeyValuePair<Item, string>> WorldItems = new Dictionary<string, KeyValuePair<Item, string>>(StringComparer.Ordinal);
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
        /// <summary>Older saves: binary "player.profile". Current: "player.profile.json" (see ProfileCodec).</summary>
        public const string PlayerSection = "player.profile";
        public const string PlayerJsonSection = PlayerSection + ProfileCodec.JsonSuffix;
        public const string BotSectionPrefix = "bot/";
        public const string WeatherSection = "weather.json";

        internal static readonly AccessTools.FieldRef<BaseLocalGame<EftGamePlayerOwner>, LocalRaidSettings> RaidSettings =
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
            result.Sections.Add(Section(PlayerJsonSection, ProfileCodec.Write(playerDescriptor)));
            state.Player = ReadPlayer(player);
            report.PlayerEffects = state.Player.Effects.Count;
            report.PlayerItemIds = player.Profile.Inventory.GetPlayerItems(EPlayerItems.All).Select(i => i.Id).Distinct().Count();
            state.Player.FoundInRaidItems = CountFoundInRaid(player.Profile);
            state.Player.Items = ItemsOf(player.Profile);
            ReadSearch(player, state.Player);
            report.PlayerMs = Lap(watch);

            // Living bots: their profiles (identity, gear, health), where they are, and what they know about the player.
            var awareness = new List<BotAwareness>();
            var groups = new Dictionary<BotsGroup, int>();
            var itemOwners = new List<KeyValuePair<string, IEnumerable<string>>>
            {
                new KeyValuePair<string, IEnumerable<string>>("player", state.Player.Items.Select(i => i.Id).ToList())
            };
            foreach (var bot in world.AllAlivePlayersList)
            {
                if (bot == null || bot.IsYourPlayer)
                {
                    continue;
                }
                try
                {
                    var descriptor = Describe(bot);
                    var bytes = ProfileCodec.Write(descriptor);
                    var botState = ReadBot(bot, player, groups, out var known);
                    result.Sections.Add(Section(BotSectionPrefix + bot.ProfileId + ProfileCodec.JsonSuffix, bytes));
                    state.Bots.Add(botState);
                    awareness.Add(known);
                    itemOwners.Add(new KeyValuePair<string, IEnumerable<string>>($"bot {bot.Profile?.Nickname} {bot.ProfileId}",
                        ItemsOf(bot.Profile).Select(i => i.Id).ToList()));
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
            // Spawn waves to come. Without them a resume holds fresh spawns instead of failing.
            try
            {
                state.Waves = Waves.Capture();
            }
            catch (Exception e)
            {
                state.Waves = null;
                Plugin.Log.LogWarning("Spawn waves not captured; a resume will hold fresh spawns: " + e);
            }
            report.BotsMs = Lap(watch);

            // World loot: loose items, containers with their current contents, corpses with their pose.
            var loot = CollectLoot(world, report, result);
            result.Sections.Add(WorldLoot.Write(loot));
            report.WorldItemIds = result.WorldItemIds.Count;

            // Every item has one place: on the player, on one bot, or in the world (corpses included).
            itemOwners.Add(new KeyValuePair<string, IEnumerable<string>>("world", result.WorldItemIds));
            var duplicates = DuplicateCheck.Find(itemOwners);
            report.DuplicateItems = duplicates.Count;
            foreach (var pair in duplicates.Take(10))
            {
                var what = result.WorldItems.TryGetValue(pair.Key, out var where) ? $"{where.Key.TemplateId} '{where.Key.ShortName}'" : "";
                Plugin.Log.LogWarning($"Duplicate item {pair.Key} {what} in: {string.Join(", ", pair.Value)}.");
            }
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

        /// <summary>What the player has searched and seen; a resume marks the same world items again.</summary>
        private static void ReadSearch(Player player, PlayerState state)
        {
            if (player.SearchController is PlayerSearchController search)
            {
                state.Searched = search._searchedItems.Where(i => i != null).Select(i => i.Id).Distinct().ToList();
            }
            if (player.SearchController is ActiveSearchController active)
            {
                state.Known = active._knownItems.Keys.Where(i => i != null).Select(i => i.Id).Distinct().ToList();
            }
        }

        /// <summary>Every item on a profile's player, with where it sits, for InventoryCheck.</summary>
        internal static List<ItemRecord> ItemsOf(Profile profile)
        {
            var records = new List<ItemRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in profile.Inventory.GetPlayerItems(EPlayerItems.All))
            {
                if (item == null || !seen.Add(item.Id))
                {
                    continue;
                }
                var container = item.CurrentAddress?.Container;
                records.Add(new ItemRecord
                {
                    Id = item.Id,
                    Template = item.TemplateId.ToString(),
                    FoundInRaid = item.SpawnedInSession,
                    // A top-level item's address points at its owner (an ItemController) in a live raid and at
                    // nothing in a rebuilt profile: both are the root.
                    Where = container == null || container is ItemController ? "(root)" : $"{container.ID} of {container.ParentItem?.Id}"
                });
            }
            return records;
        }

        /// <summary>Logs how a profile's items compare with the save's list; returns whether they match.</summary>
        internal static bool CheckInventory(string when, List<ItemRecord> saved, Profile profile)
        {
            if (saved == null || saved.Count == 0 || profile == null)
            {
                Plugin.Log.LogInfo($"{when}: the save has no item list to check against.");
                return true;
            }
            var diff = InventoryCheck.Compare(saved, ItemsOf(profile));
            if (diff.Clean)
            {
                Plugin.Log.LogInfo($"{when}: {diff.Summary()}.");
                return true;
            }
            Plugin.Log.LogWarning($"{when}: {diff.Summary()}.");
            foreach (var line in diff.Details(20))
            {
                Plugin.Log.LogWarning($"{when}: {line}");
            }
            return false;
        }

        internal static int CountFoundInRaid(Profile profile)
        {
            return profile.Inventory.GetPlayerItems(EPlayerItems.All).Where(i => i.SpawnedInSession).Select(i => i.Id).Distinct().Count();
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

        private static void AddLive(Item root, CaptureResult result, string where)
        {
            if (root == null)
            {
                return;
            }
            var items = root is ContainerCollection collection ? collection.GetAllItemsFromCollection() : new[] { root };
            foreach (var item in items)
            {
                result.WorldItemIds.Add(item.Id);
                result.WorldItems[item.Id] = new KeyValuePair<Item, string>(item, where);
            }
        }

        private static List<JsonLootItem> CollectLoot(GameWorld world, CaptureReport report, CaptureResult result)
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
                            AddLive(root, result, "corpse of " + corpse.PlayerProfileID);
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
                            AddLive(root, result, "loose item");
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
                            AddLive(root, result, "container " + container.Id);
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

        private static BotState ReadBot(Player bot, Player mainPlayer, Dictionary<BotsGroup, int> groups, out BotAwareness awareness)
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
            if (owner != null)
            {
                var group = owner.BotsGroup;
                if (group != null)
                {
                    if (!groups.TryGetValue(group, out var number))
                    {
                        number = groups.Count + 1;
                        groups.Add(group, number);
                    }
                    state.Group = number;
                    state.GroupTarget = group.TargetMembersCount;
                }
                if (owner.Boss != null && owner.Boss.IamBoss)
                {
                    state.Boss = true;
                    state.FollowersTarget = owner.Boss.TargetFollowersCount;
                }
                state.Zone = owner.SpawnBotZone != null ? owner.SpawnBotZone.NameZone : null;
                state.Personality = SainCompat.ReadPersonality(owner);
            }
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
                var painKiller = effect as ActiveHealthController.PainKiller;
                effects.Add(new EffectState
                {
                    ItemTemplateId = painKiller?.ItemTemplateId,
                    MaxDuration = painKiller?.MaxDuration ?? 0f,
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
                doors.Add(new DoorState { Id = obj.Id, Kind = obj.GetType().Name, State = (int)obj.DoorState, Broken = obj is Door door && door.IsBroken });
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
        internal static string GameVersion()
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
