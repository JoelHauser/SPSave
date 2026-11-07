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
        /// <summary>Game started; the raid is being ended as Killed or MIA (ResolveController). Not frozen.</summary>
        Resolving,
        /// <summary>The player took control, or the resolve ended the raid; the save is used up.</summary>
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

        /// <summary>
        /// Null: a normal resume. Killed or MissingInAction: load the raid and end it that way (ResolveController).
        /// Chosen in Begin: the save's manifest (a combat save is always Killed), else the menu's request (Abandon).
        /// </summary>
        internal static ExitStatus? Outcome;

        /// <summary>Set by the menu's Abandon before it starts the raid; taken by the next raid start.</summary>
        internal static ResolveOutcome? Requested;

        /// <summary>Saved living bots' profiles by profile id, rebuilt from their sections.</summary>
        internal static readonly Dictionary<string, Profile> BotProfiles = new Dictionary<string, Profile>(StringComparer.Ordinal);

        /// <summary>Set once the saved waves replaced the server's (LocalGameCreatePatch).</summary>
        internal static bool WavesRestored;

        /// <summary>
        /// Fresh spawns are held in a resumed raid whose waves couldn't be restored (a save from before 0.4.0, or a
        /// failure): the server's waves would start again from zero, and in a raid being ended as Killed/MIA, which
        /// has no bots at all. Holds until the next raid starts.
        /// </summary>
        internal static bool HoldFreshSpawns => Phase != ResumePhase.None && (!WavesRestored || Outcome != null);

        internal static bool IsRestoredBot(Profile profile) =>
            profile != null && profile.Id != null && BotProfiles.TryGetValue(profile.Id, out var restored) && ReferenceEquals(restored, profile);

        /// <summary>Found-in-raid flags of every saved world item, by reference: GameWorld.SpawnLoot(initial) sets them all.</summary>
        private static readonly Dictionary<Item, bool> FoundInRaid = new Dictionary<Item, bool>();

        internal static bool Loading => Phase == ResumePhase.Loading;
        internal static bool Active => Phase == ResumePhase.Loading || Phase == ResumePhase.WaitingForReady || Phase == ResumePhase.Resolving;

        internal static void TryBegin(TarkovApplication app)
        {
            var requested = Requested;
            Requested = null;
            Reset();
            if (!Plugin.ResumeEnabled.Value)
            {
                return;
            }
            try
            {
                Begin(app, requested);
            }
            catch (Exception e)
            {
                Reset();
                Plugin.Log.LogError("Resume failed while loading the save; starting a normal raid. " + e);
                NotificationManager.DisplayWarningNotification("SPSave: the saved raid could not be loaded. Starting a fresh raid.");
            }
        }

        private static void Begin(TarkovApplication app, ResolveOutcome? requested)
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

            // Living bots. One that can't be rebuilt is left out of the resume rather than stopping it.
            var botProfiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
            foreach (var section in sections)
            {
                if (!section.Key.StartsWith(RaidCapture.BotSectionPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    var bot = new Profile(ProfileCodec.Read(section.Key, section.Value));
                    botProfiles[bot.Id] = bot;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Saved bot {section.Key} can't be rebuilt and won't come back: {e.Message}");
                }
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
            foreach (var pair in botProfiles)
            {
                BotProfiles[pair.Key] = pair.Value;
            }
            foreach (var bot in state.Bots)
            {
                if (!string.IsNullOrEmpty(bot.Personality) && BotProfiles.ContainsKey(bot.ProfileId))
                {
                    SainCompat.Pinned[bot.ProfileId] = bot.Personality;
                }
            }
            var outcome = ResolveRule.Choose(manifest.ResolveAs, requested);
            Outcome = outcome == null ? (ExitStatus?)null
                : outcome == ResolveOutcome.Killed ? ExitStatus.Killed : ExitStatus.MissingInAction;
            // Last: nothing above changes the game, so a failure before this line leaves a normal raid.
            session.Profile = profile;
            Phase = ResumePhase.Loading;
            if (Outcome != null)
            {
                Plugin.Log.LogInfo($"This load ends the saved raid as {Outcome} ({(manifest.ResolveAs != null ? "saved in combat" : "requested")}).");
                NotificationManager.DisplayMessageNotification($"SPSave: ending your saved raid on {map}.");
                return;
            }

            Plugin.Log.LogInfo($"Resuming the raid on {map} saved {manifest.CreatedUtc} (from {from}): {loot.Count} world entries, " +
                               $"{state.Doors.Count} doors, {BotProfiles.Count}/{state.Bots.Count} living bots, " +
                               $"{state.Clock?.RemainingSeconds:F0} s left, " +
                               (state.Waves == null ? "no saved waves (fresh spawns held)." : $"{state.Waves.Boss.Count} boss and {state.Waves.Old.Count} old-style waves saved."));
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

        /// <summary>
        /// The player took control: the save's data is done with, but restored bots' personalities and profiles stay
        /// until the next raid. SAIN sets a bot's personality when it activates and again on preset changes, which can
        /// come after Ready (the 0.3.0 test cleared them here, so the pins were gone when SAIN asked).
        /// </summary>
        internal static void GoLive()
        {
            SavePath = null;
            Manifest = null;
            State = null;
            Profile = null;
            Loot = null;
            Weather = null;
            FoundInRaid.Clear();
            Phase = ResumePhase.Live;
        }

        internal static void Reset()
        {
            Phase = ResumePhase.None;
            Outcome = null;
            ResolveBlackout.End();
            SavePath = null;
            Manifest = null;
            State = null;
            Profile = null;
            Loot = null;
            Weather = null;
            BotProfiles.Clear();
            WavesRestored = false;
            SainCompat.Pinned.Clear();
            SainCompat.Logged.Clear();
            FreshSpawnGate.Reset();
            FoundInRaid.Clear();
        }

        /// <summary>Gear, heads and clothing of every saved living bot; a normal spawn loads them with the bot's profile.</summary>
        internal static IEnumerable<ResourceKey> BotBundles()
        {
            if (Outcome != null)
            {
                // Ending the raid: no bots come back (RaidRestore), so none of their gear is needed.
                return new List<ResourceKey>();
            }
            return BotProfiles.Values.SelectMany(bot => bot.GetAllPrefabPaths()).ToList();
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
