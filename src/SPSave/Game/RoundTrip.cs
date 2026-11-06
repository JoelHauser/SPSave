using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using EFT.BinarySerialization;
using EFT.Weather;
using Mirror;
using Newtonsoft.Json;

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
        public static RoundTripResult Verify(Dictionary<string, byte[]> sections, HashSet<string> liveWorldIds)
        {
            var problems = new List<string>();
            var profiles = 0;

            foreach (var section in sections)
            {
                if (section.Key != RaidCapture.PlayerSection && !section.Key.StartsWith(RaidCapture.BotSectionPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                profiles++;
                try
                {
                    var descriptor = new NetworkReader(new ArraySegment<byte>(section.Value)).ReadEFTProfileDescriptor();
                    var again = RaidCapture.Binary(w => w.WriteEFTProfileDescriptor(descriptor));
                    if (!section.Value.SequenceEqual(again))
                    {
                        problems.Add($"{section.Key} re-encodes to {again.Length} bytes, saved {section.Value.Length}");
                    }
                }
                catch (Exception e)
                {
                    problems.Add($"{section.Key} does not decode: {e.Message}");
                }
            }

            var worldIds = 0;
            if (sections.TryGetValue(RaidCapture.LootSection, out var lootBytes))
            {
                try
                {
                    var descriptor = new NetworkReader(new ArraySegment<byte>(lootBytes)).ReadEFTLootDataDescriptor();
                    var again = RaidCapture.Binary(w => w.WriteEFTLootDataDescriptor(descriptor));
                    if (!lootBytes.SequenceEqual(again))
                    {
                        problems.Add($"world loot re-encodes to {again.Length} bytes, saved {lootBytes.Length}");
                    }

                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var entry in ItemBinarySerializer.DeserializeLootData(descriptor))
                    {
                        RaidCapture.AddIds(entry.Item, ids);
                    }
                    worldIds = ids.Count;
                    var missing = liveWorldIds.Count(id => !ids.Contains(id));
                    var extra = ids.Count(id => !liveWorldIds.Contains(id));
                    if (missing > 0 || extra > 0)
                    {
                        problems.Add($"world items differ from live: {missing} missing, {extra} extra");
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

            return new RoundTripResult
            {
                Ok = problems.Count == 0,
                Detail = problems.Count == 0
                    ? $"{profiles} profiles and {worldIds} world items round-trip"
                    : string.Join("; ", problems)
            };
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
