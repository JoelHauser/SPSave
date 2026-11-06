using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using EFT.BinarySerialization;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Weather;
using JsonType;
using Mirror;
using Newtonsoft.Json;
using SPSave.Core;

namespace SPSave.Game
{
    internal enum ResumePhase
    {
        None,
        /// <summary>From the raid start to the moment the game starts: the hooks substitute saved state.</summary>
        Loading,
        /// <summary>Game started, world restored and frozen until the player takes control.</summary>
        WaitingForReady,
        /// <summary>The player took control; the save is used up.</summary>
        Live
    }

    /// <summary>
    /// The resume in progress. Begins when the player starts a PMC raid on the map a save was made on: the saved
    /// profile replaces Session.Profile before TarkovApplication.LocalGameCreate reads it, so everything downstream
    /// (the raid, insurance, the results screen's stats) uses it. After the raid the client reloads the profile from
    /// the server (Session.GetProfiles), as usual.
    /// </summary>
    internal static class ResumeSession
    {
        internal static ResumePhase Phase;
        internal static string SavePath;
        internal static Manifest Manifest;
        internal static RaidState State;
        internal static Profile Profile;
        internal static LootData Loot;
        internal static WeatherNode[] Weather;

        /// <summary>Found-in-raid flags of every saved world item, by reference: GameWorld.SpawnLoot(initial) sets them all.</summary>
        private static readonly Dictionary<Item, bool> FoundInRaid = new Dictionary<Item, bool>();

        internal static bool Loading => Phase == ResumePhase.Loading;
        internal static bool Active => Phase == ResumePhase.Loading || Phase == ResumePhase.WaitingForReady;

        internal static void TryBegin(TarkovApplication app)
        {
            Reset();
            if (!Plugin.ResumeEnabled.Value)
            {
                return;
            }
            try
            {
                Begin(app);
            }
            catch (Exception e)
            {
                Reset();
                Plugin.Log.LogError("Resume failed while loading the save; starting a normal raid. " + e);
                NotificationManager.DisplayWarningNotification("SPSave: the saved raid could not be loaded. Starting a fresh raid.");
            }
        }

        private static void Begin(TarkovApplication app)
        {
            var raid = app._raidSettings;
            var session = app.Session as ClientBackendSession;
            var current = session?.Profile;
            if (raid == null || current == null || raid.Side != ESideType.Pmc)
            {
                return;
            }

            var path = Plugin.SavePath(current.Id);
            var problems = new List<string>();
            var sections = SnapshotFile.LoadNewestValid(path, out var from, problems);
            foreach (var problem in problems)
            {
                Plugin.Log.LogWarning("Save not usable: " + problem);
            }
            if (sections == null)
            {
                return;
            }

            var manifest = Parse<Manifest>(sections, RaidCapture.ManifestSection);
            var map = raid.SelectedLocation?.Id ?? raid.LocationId;
            if (!string.Equals(manifest.Map, map, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogInfo($"Save is for {manifest.Map}; this raid is {map}. Starting a fresh raid.");
                NotificationManager.DisplayMessageNotification($"SPSave: your saved raid is on {manifest.Map}. Starting a fresh raid on {map}.");
                return;
            }
            if (manifest.ProfileId != current.Id)
            {
                Plugin.Log.LogWarning($"Save belongs to profile {manifest.ProfileId}, not {current.Id}. Not resuming.");
                return;
            }
            var gameVersion = RaidCapture.GameVersion();
            if (manifest.GameVersion != gameVersion)
            {
                Plugin.Log.LogWarning($"Save was made on game {manifest.GameVersion}; this is {gameVersion}. Not resuming.");
                NotificationManager.DisplayWarningNotification("SPSave: the saved raid is from a different game version and can't be resumed.");
                return;
            }

            var state = Parse<RaidState>(sections, RaidCapture.StateSection);
            var playerSection = sections.ContainsKey(RaidCapture.PlayerJsonSection) ? RaidCapture.PlayerJsonSection : RaidCapture.PlayerSection;
            var descriptor = ProfileCodec.Read(playerSection, sections[playerSection]);
            var profile = new Profile(descriptor);
            RaidCapture.CheckInventory("Profile rebuilt from the save", state.Player?.Items, profile);
            var loot = WorldLoot.Read(sections);
            WeatherNode[] weather = null;
            if (sections.TryGetValue(RaidCapture.WeatherSection, out var weatherBytes))
            {
                weather = Encoding.UTF8.GetString(weatherBytes).ParseJsonTo<WeatherNode[]>();
            }

            FoundInRaid.Clear();
            foreach (var entry in loot)
            {
                if (entry.Item is ContainerCollection collection)
                {
                    foreach (var item in collection.GetAllItemsFromCollection())
                    {
                        FoundInRaid[item] = item.SpawnedInSession;
                    }
                }
                else if (entry.Item != null)
                {
                    FoundInRaid[entry.Item] = entry.Item.SpawnedInSession;
                }
            }

            SavePath = path;
            Manifest = manifest;
            State = state;
            Profile = profile;
            Loot = loot;
            Weather = weather;
            // Last: nothing above changes the game, so a failure before this line leaves a normal raid.
            session.Profile = profile;
            Phase = ResumePhase.Loading;

            Plugin.Log.LogInfo($"Resuming the raid on {map} saved {manifest.CreatedUtc} (from {from}): {loot.Count} world entries, " +
                               $"{state.Doors.Count} doors, {state.Clock?.RemainingSeconds:F0} s left. Bots are not restored in this build.");
            NotificationManager.DisplayMessageNotification($"SPSave: resuming your saved raid on {map}.");
        }

        /// <summary>Head, body, feet and other visible parts of every saved corpse, as Profile.GetAllPrefabPaths resolves them.</summary>
        internal static List<ResourceKey> CorpseBundles()
        {
            var keys = new List<ResourceKey>();
            var solver = Comfort.Common.Singleton<CustomizationSolver>.Instance;
            if (solver == null || Loot == null)
            {
                return keys;
            }
            foreach (var corpse in Loot.OfType<JsonCorpse>())
            {
                if (corpse.Customization == null)
                {
                    continue;
                }
                foreach (var part in corpse.Customization.GetPartsWithVisual())
                {
                    var key = solver.GetBundle(part.Value);
                    if (key != null && !string.IsNullOrEmpty(key.path))
                    {
                        keys.Add(key);
                    }
                }
            }
            return keys;
        }

        /// <summary>After GameWorld.SpawnLoot(initial: true) marked everything found in raid: put the saved flags back.</summary>
        internal static void RestoreFoundInRaid()
        {
            var changed = 0;
            foreach (var pair in FoundInRaid)
            {
                if (pair.Key.SpawnedInSession != pair.Value)
                {
                    pair.Key.SpawnedInSession = pair.Value;
                    changed++;
                }
            }
            Plugin.Log.LogInfo($"Restored found-in-raid flags on {FoundInRaid.Count} world items ({changed} were changed by the spawn).");
            FoundInRaid.Clear();
        }

        internal static void Reset()
        {
            Phase = ResumePhase.None;
            SavePath = null;
            Manifest = null;
            State = null;
            Profile = null;
            Loot = null;
            Weather = null;
            FoundInRaid.Clear();
        }

        private static T Parse<T>(Dictionary<string, byte[]> sections, string name)
        {
            if (!sections.TryGetValue(name, out var bytes))
            {
                throw new InvalidOperationException($"Save has no {name}.");
            }
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes))
                   ?? throw new InvalidOperationException($"{name} is empty.");
        }
    }
}
