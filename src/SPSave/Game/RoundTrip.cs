using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using EFT.BinarySerialization;
using EFT.Weather;
using Mirror;
using Newtonsoft.Json;
using SPSave.Core;

namespace SPSave.Game
{
    internal sealed class RoundTripResult
    {
        public bool Ok;
        public string Detail;
    }

    /// <summary>
    /// Proves a save file decodes into what was captured, using sections read back from disk:
    /// every profile and the world loot re-encode to the same bytes, the world loot deserializes into items with the
    /// same id set as the live world, and the JSON sections parse. Step A only: a restore (Step B) will build Profile
    /// objects and spawn loot, which this does not.
    /// </summary>
    internal static class RoundTrip
    {
        public static RoundTripResult Verify(Dictionary<string, byte[]> sections, HashSet<string> liveWorldIds,
            Dictionary<string, KeyValuePair<EFT.InventoryLogic.Item, string>> liveItems = null, int expectedCorpses = -1)
        {
            var problems = new List<string>();
            var profiles = 0;

            foreach (var section in sections)
            {
                if (!ProfileCodec.IsProfileSection(section.Key))
                {
                    continue;
                }
                profiles++;
                try
                {
                    if (ProfileCodec.Read(section.Key, section.Value) == null)
                    {
                        problems.Add($"{section.Key} reads back empty");
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"{section.Key} does not decode: {e.Message}");
                }
            }

            var worldIds = 0;
            if (WorldLoot.Has(sections))
            {
                try
                {
                    var loot = WorldLoot.Read(sections);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var entry in loot)
                    {
                        RaidCapture.AddIds(entry.Item, ids);
                    }
                    if (expectedCorpses >= 0)
                    {
                        var corpses = loot.Count(entry => entry is JsonType.JsonCorpse);
                        if (corpses != expectedCorpses)
                        {
                            problems.Add($"{corpses} corpses read back, {expectedCorpses} saved");
                        }
                    }
                    worldIds = ids.Count;
                    var missing = liveWorldIds.Count(id => !ids.Contains(id));
                    var extra = ids.Count(id => !liveWorldIds.Contains(id));
                    if (missing > 0 || extra > 0)
                    {
                        problems.Add($"world items differ from live: {missing} missing, {extra} extra");
                        if (liveItems != null)
                        {
                            foreach (var id in liveWorldIds.Where(id => !ids.Contains(id)).Take(10))
                            {
                                if (liveItems.TryGetValue(id, out var live))
                                {
                                    Plugin.Log.LogWarning("Lost in the save: " + Describe(live.Key) + ", " + live.Value);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"world loot does not decode: {e.Message}");
                }
            }
            else
            {
                problems.Add("no world loot section");
            }

            TryParse<Manifest>(sections, RaidCapture.ManifestSection, problems);
            TryParse<RaidState>(sections, RaidCapture.StateSection, problems);

            // The profile must rebuild with every item on the player where it was: a resume builds it the same way.
            try
            {
                if (sections.TryGetValue(RaidCapture.PlayerJsonSection, out var playerBytes) && sections.TryGetValue(RaidCapture.StateSection, out var stateBytes))
                {
                    var state = JsonConvert.DeserializeObject<RaidState>(Encoding.UTF8.GetString(stateBytes));
                    var descriptor = ProfileCodec.Read(RaidCapture.PlayerJsonSection, playerBytes);
                    if (!RaidCapture.CheckInventory("Save check", state?.Player?.Items, new Profile(descriptor)))
                    {
                        problems.Add("the profile rebuilds with different items (see the lines above)");
                    }
                }
            }
            catch (Exception e)
            {
                problems.Add($"the profile does not rebuild: {e.Message}");
            }
            if (sections.TryGetValue(RaidCapture.WeatherSection, out var weatherBytes))
            {
                try
                {
                    var nodes = Encoding.UTF8.GetString(weatherBytes).ParseJsonTo<WeatherNode[]>();
                    if (nodes == null || nodes.Length == 0)
                    {
                        problems.Add("weather nodes parse to nothing");
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"weather does not parse: {e.Message}");
                }
            }

            CheckWaves(sections);

            return new RoundTripResult
            {
                Ok = problems.Count == 0,
                Detail = problems.Count == 0
                    ? $"{profiles} profiles and {worldIds} world items round-trip"
                    : string.Join("; ", problems)
            };
        }

        /// <summary>
        /// Saved boss waves must read back in the order their state was saved. Only a warning: a resume without them
        /// still works, it just spawns no more boss or PMC waves.
        /// </summary>
        private static void CheckWaves(Dictionary<string, byte[]> sections)
        {
            try
            {
                if (!sections.TryGetValue(RaidCapture.StateSection, out var stateBytes))
                {
                    return;
                }
                var waves = JsonConvert.DeserializeObject<RaidState>(Encoding.UTF8.GetString(stateBytes))?.Waves;
                if (waves == null)
                {
                    Plugin.Log.LogWarning("Save check: no spawn waves saved; a resume will hold fresh spawns.");
                    return;
                }
                // Waves.Restore needs every boss wave's type name; Init parses it.
                var unnamed = waves.Boss?.Count(w => string.IsNullOrEmpty(w.BossName)) ?? 0;
                if (unnamed > 0)
                {
                    Plugin.Log.LogWarning($"Save check: {unnamed} saved boss waves have no type; a resume will spawn no more boss or PMC waves.");
                    return;
                }
                var toCome = waves.Boss?.Count(w => WaveTrim.Boss(w.Time, !string.IsNullOrEmpty(w.TriggerName), w.ShallSpawn, w.Activated, waves.BossElapsed) != null) ?? 0;
                Plugin.Log.LogInfo($"Save check: {waves.Boss?.Count ?? 0} boss waves ({toCome} still to come, {waves.BossElapsed:F0} s in) " +
                                   $"and {waves.Old.Count} old-style waves ({waves.OldElapsed:F0} s in) read back.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Save check: spawn waves don't read back; a resume will spawn no more boss or PMC waves: " + e.Message);
            }
        }

        private static string Describe(EFT.InventoryLogic.Item item)
        {
            var container = item.CurrentAddress?.Container;
            var parent = container?.ParentItem;
            return $"{item.Id} {item.TemplateId} '{item.ShortName}' ({item.GetType().Name}) in '{container?.ID}' " +
                   $"({container?.GetType().Name}) of {parent?.TemplateId} '{parent?.ShortName}' ({parent?.GetType().Name})";
        }

        private static void TryParse<T>(Dictionary<string, byte[]> sections, string name, List<string> problems)
        {
            if (!sections.TryGetValue(name, out var bytes))
            {
                problems.Add($"no {name}");
                return;
            }
            try
            {
                if (JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes)) == null)
                {
                    problems.Add($"{name} parses to nothing");
                }
            }
            catch (Exception e)
            {
                problems.Add($"{name} does not parse: {e.Message}");
            }
        }
    }
}
