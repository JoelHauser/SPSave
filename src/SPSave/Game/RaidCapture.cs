using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Weather;
using HarmonyLib;
using JsonType;
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
    /// One raid moment as data (RaidCapture.Snapshot): nothing in it is read from the game afterwards, except
    /// Result.WorldItems, which names live items for logging and stays on the main thread.
    /// </summary>
    internal sealed class CaptureSnapshot
    {
        public CaptureResult Result;
        public Manifest Manifest;
        public RaidState State;
        /// <summary>The player's profile; its inventory is already flattened (InventoryDescriptor._items).</summary>
        public ProfileDescriptor Player;
        /// <summary>Bot profiles by section name, flattened like the player's.</summary>
        public readonly List<KeyValuePair<string, ProfileDescriptor>> Bots = new List<KeyValuePair<string, ProfileDescriptor>>();
        /// <summary>World loot as LootItemSerializer records, what the game's JSON converter writes for each entry.</summary>
        public readonly List<object> Loot = new List<object>();
        public WeatherNode[] Weather;
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

        /// <summary>Capture and serialize in one go, all on the main thread (Save &amp; Quit).</summary>
        public static CaptureResult Run(float combatWindowSeconds)
        {
            var snapshot = Snapshot(combatWindowSeconds);
            Serialize(snapshot);
            return snapshot.Result;
        }

        /// <summary>
        /// Reads the raid in this frame into data that no longer refers to the game: profile descriptors with their
        /// inventories flattened (FlatItem[]), loot as LootItemSerializer records (FlatItem[] too), the raid state and
        /// manifest as SPSave's own objects. <see cref="Serialize"/> turns it into sections and touches nothing else,
        /// so it can run off the main thread (autosave). Every live read happens here, in one frame: one moment.
        /// </summary>
        public static CaptureSnapshot Snapshot(float combatWindowSeconds)
        {
            var world = Singleton<GameWorld>.Instance;
            var player = world != null ? world.MainPlayer : null;
            if (player == null)
            {
                throw new InvalidOperationException("No raid in progress.");
            }

            var result = new CaptureResult { ProfileId = player.ProfileId };
            var snapshot = new CaptureSnapshot { Result = result };
            var report = result.Report;
            report.Utc = DateTime.UtcNow;
            report.Map = world.LocationId ?? "";
            report.ProfileId = player.ProfileId;
            var state = new RaidState();
            snapshot.State = state;
            var watch = Stopwatch.StartNew();

            // Player: profile as GameEnd would send it, with live health instead of the pre-raid copy.
            var step = Stopwatch.StartNew();
            snapshot.Player = Describe(player);
            report.Step("player describe", Lap(step));
            Flatten(snapshot.Player);
            report.Step("player flatten", Lap(step));
            state.Player = ReadPlayer(player);
            report.PlayerEffects = state.Player.Effects.Count;
            report.PlayerItemIds = player.Profile.Inventory.GetPlayerItems(EPlayerItems.All).Select(i => i.Id).Distinct().Count();
            state.Player.FoundInRaidItems = CountFoundInRaid(player.Profile);
            state.Player.Items = ItemsOf(player.Profile);
            ReadSearch(player, state.Player);
            report.Step("player state", Lap(step));
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
                    step.Restart();
                    var descriptor = Describe(bot);
                    report.Step("bots describe", Lap(step));
                    Flatten(descriptor);
                    report.Step("bots flatten", Lap(step));
                    var botState = ReadBot(bot, player, groups, out var known);
                    snapshot.Bots.Add(new KeyValuePair<string, ProfileDescriptor>(BotSectionPrefix + bot.ProfileId + ProfileCodec.JsonSuffix, descriptor));
                    state.Bots.Add(botState);
                    awareness.Add(known);
                    itemOwners.Add(new KeyValuePair<string, IEnumerable<string>>($"bot {bot.Profile?.Nickname} {bot.ProfileId}",
                        ItemsOf(bot.Profile).Select(i => i.Id).ToList()));
                    report.Step("bots state", Lap(step));
                }
                catch (Exception e)
                {
                    report.SkippedBots++;
                    Plugin.Log.LogWarning($"Bot {bot.ProfileId} not captured: {e}");
                }
            }
            report.BotsAlive = state.Bots.Count;
            step.Restart();
            // Pit FireTeam: which of those bots are the player's followers (refuses the save if it can't tell).
            var pitWatch = Stopwatch.StartNew();
            state.PitFollowers = PitFireTeamCompat.Capture(player);
            var pit = PitFireTeamCompat.Describe(state.PitFollowers);
            if (pit != null)
            {
                Plugin.Log.LogInfo(pit + (state.PitFollowers.Count > 0 ? ": " + string.Join(", ", state.PitFollowers.Select(f => $"{f.Nickname} ({f.Kind})")) : "") +
                                   $" ({pitWatch.Elapsed.TotalMilliseconds:F0} ms).");
            }
            var verdict = CombatRule.Evaluate(awareness, combatWindowSeconds);
            state.Combat = new CombatState { WindowSeconds = combatWindowSeconds, InCombat = verdict.InCombat, Detail = verdict.Describe() };
            report.InCombat = verdict.InCombat;
            report.CombatDetail = verdict.Describe();
            state.RaidReviewElapsedMs = RaidReviewCompat.ReadElapsedMs();
            report.Step("pit+combat", Lap(step));
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
            report.Step("waves", Lap(step));
            report.BotsMs = Lap(watch);

            // World loot: loose items, containers with their current contents, corpses with their pose. Turned into
            // the records the game's own JSON converter writes (JsonConverterAdapter: LootItemSerializer.Serialize,
            // which flattens the live items), so the JSON itself can be written later.
            var loot = CollectLoot(world, report, result);
            report.Step("loot collect", Lap(step));
            foreach (var entry in loot)
            {
                snapshot.Loot.Add(entry is JsonCorpse corpse
                    ? ((ISerializer<JsonCorpse>)new LootItemSerializer()).Serialize(corpse)
                    : ((ISerializer<JsonLootItem>)new LootItemSerializer()).Serialize(entry));
            }
            report.Step("loot flatten", Lap(step));
            report.WorldItemIds = result.WorldItemIds.Count;

            // Found-in-raid ammo (Actually Found In Raid): a rebuild loses it, so the save lists it (AmmoFoundInRaid).
            var firAmmo = new HashSet<string>(StringComparer.Ordinal);
            AmmoFoundInRaid.Collect(AmmoFoundInRaid.ItemsOf(player.Profile), firAmmo);
            foreach (var bot in world.AllAlivePlayersList)
            {
                if (bot != null && !bot.IsYourPlayer)
                {
                    AmmoFoundInRaid.Collect(AmmoFoundInRaid.ItemsOf(bot.Profile), firAmmo);
                }
            }
            AmmoFoundInRaid.Collect(result.WorldItems.Values.Select(v => v.Key), firAmmo);
            state.FoundInRaidAmmo = firAmmo.ToList();
            report.Step("fir ammo", Lap(step));

            // Every item has one place: on the player, on one bot, or in the world (corpses included).
            itemOwners.Add(new KeyValuePair<string, IEnumerable<string>>("world", result.WorldItemIds));
            var duplicates = DuplicateCheck.Find(itemOwners);
            report.Step("duplicates", Lap(step));
            report.DuplicateItems = duplicates.Count;
            foreach (var pair in duplicates.Take(10))
            {
                var what = result.WorldItems.TryGetValue(pair.Key, out var where) ? $"{where.Key.TemplateId} '{where.Key.ShortName}'" : "";
                Plugin.Log.LogWarning($"Duplicate item {pair.Key} {what} in: {string.Join(", ", pair.Value)}.");
            }
            report.LootMs = Lap(watch);

            // Doors, raid clock, time of day, weather.
            step.Restart();
            state.Doors = ReadDoors();
            report.Doors = state.Doors.Count;
            report.Step("doors", Lap(step));
            var nodes = WeatherNodesPatch.Last;
            state.Clock = ReadClock(world, nodes);
            report.Weather = !state.Clock.HasWeather ? "none on this map"
                : state.Clock.WeatherNodes == 0 ? "NODES MISSING"
                : $"{state.Clock.WeatherNodes} nodes{(state.Clock.WeatherMatchesCurve ? "" : ", curve differs")}";
            snapshot.Weather = nodes?.ToArray();
            snapshot.Manifest = BuildManifest(world, player);
            report.Step("clock", Lap(step));
            report.WorldMs = Lap(watch);
            return snapshot;
        }

        /// <summary>
        /// The snapshot as save sections, in the order Save &amp; Quit has always written them. Reads only the snapshot
        /// (no game objects), so it is safe off the main thread. Runs the game's JSON converters, which for these types
        /// only copy data: WeatherSerializer, MongoID, enums, UnparsedData.
        /// </summary>
        public static void Serialize(CaptureSnapshot snapshot)
        {
            var result = snapshot.Result;
            var report = result.Report;
            var watch = Stopwatch.StartNew();
            var step = Stopwatch.StartNew();
            result.Sections.Clear();
            result.Sections.Add(Section(ManifestSection, Json(snapshot.Manifest)));
            result.Sections.Add(Section(PlayerJsonSection, ProfileCodec.Write(snapshot.Player)));
            report.Step("player json", Lap(step));
            report.PlayerMs += Lap(watch);
            foreach (var bot in snapshot.Bots)
            {
                result.Sections.Add(Section(bot.Key, ProfileCodec.Write(bot.Value)));
            }
            report.Step("bots json", Lap(step));
            report.BotsMs += Lap(watch);
            result.Sections.Add(new KeyValuePair<string, byte[]>(WorldLoot.JsonSection,
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot.Loot.ToArray(), EftJsonConverters.Converters))));
            report.Step("loot json", Lap(step));
            report.LootMs += Lap(watch);
            if (snapshot.Weather != null)
            {
                result.Sections.Add(Section(WeatherSection, Encoding.UTF8.GetBytes(snapshot.Weather.ToJson())));
            }
            report.Step("weather json", Lap(step));
            report.WorldMs += Lap(watch);
            result.Sections.Add(Section(StateSection, Json(snapshot.State)));
            report.StateMs = Lap(watch);
        }

        /// <summary>
        /// Flattens the descriptor's inventory now (InventoryDescriptor.OnJSONSerializing, which ToJson would run
        /// first anyway: it returns at once once _items is set), so its cost is timed apart from the JSON writing.
        /// </summary>
        private static void Flatten(ProfileDescriptor descriptor)
        {
            descriptor.Inventory?.OnJSONSerializing(default(System.Runtime.Serialization.StreamingContext));
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
                    IsAmmo = item is Ammo,
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
            if (diff.HasWarnings)
            {
                foreach (var line in diff.Warnings(20))
                {
                    Plugin.Log.LogWarning($"{when}: {line}");
                }
            }
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
