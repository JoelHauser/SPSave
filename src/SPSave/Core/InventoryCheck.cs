using System;
using System.Collections.Generic;
using System.Linq;

namespace SPSave.Core
{
    /// <summary>One item on the player, as the save recorded it.</summary>
    public sealed class ItemRecord
    {
        public string Id;
        public string Template;
        public bool FoundInRaid;
        /// <summary>The container it sits in and that container's item, e.g. "main of 6aee…".</summary>
        public string Where;
        /// <summary>A round of ammo: its found-in-raid flag is only a warning (AmmoFoundInRaid). False in saves before 1.0.3.</summary>
        public bool IsAmmo;

        public override string ToString()
        {
            return $"{Id} {Template} in {Where}{(FoundInRaid ? " (found in raid)" : "")}";
        }
    }

    public sealed class InventoryDiff
    {
        public readonly List<ItemRecord> Missing = new List<ItemRecord>();
        public readonly List<ItemRecord> Extra = new List<ItemRecord>();
        public readonly List<KeyValuePair<ItemRecord, ItemRecord>> FoundInRaidChanged = new List<KeyValuePair<ItemRecord, ItemRecord>>();
        /// <summary>
        /// Ammo whose found-in-raid flag differs. In the game ammo is never found in raid; only another mod (Actually
        /// Found In Raid) makes it so, so a difference loses that mod's flag on a few rounds and doesn't fail a save.
        /// </summary>
        public readonly List<KeyValuePair<ItemRecord, ItemRecord>> AmmoFoundInRaidChanged = new List<KeyValuePair<ItemRecord, ItemRecord>>();
        public readonly List<KeyValuePair<ItemRecord, ItemRecord>> Moved = new List<KeyValuePair<ItemRecord, ItemRecord>>();
        public int Saved;
        public int Now;

        public bool Clean => Missing.Count == 0 && Extra.Count == 0 && FoundInRaidChanged.Count == 0 && Moved.Count == 0;

        public string Summary()
        {
            return Clean
                ? $"all {Saved} items on the player match the save"
                : $"{Now} items on the player vs {Saved} saved: {Missing.Count} missing, {Extra.Count} extra, " +
                  $"{FoundInRaidChanged.Count} changed found-in-raid, {Moved.Count} in a different place";
        }

        public bool HasWarnings => AmmoFoundInRaidChanged.Count > 0;

        public IEnumerable<string> Warnings(int limit)
        {
            yield return $"{AmmoFoundInRaidChanged.Count} rounds of ammo changed found-in-raid (not a failure)";
            foreach (var pair in AmmoFoundInRaidChanged.Take(limit)) yield return $"ammo found-in-raid {pair.Key.FoundInRaid} -> {pair.Value.FoundInRaid}: {pair.Value}";
        }

        public IEnumerable<string> Details(int limit)
        {
            foreach (var item in Missing.Take(limit)) yield return "missing: " + item;
            foreach (var item in Extra.Take(limit)) yield return "extra: " + item;
            foreach (var pair in FoundInRaidChanged.Take(limit)) yield return $"found-in-raid {pair.Key.FoundInRaid} -> {pair.Value.FoundInRaid}: {pair.Value}";
            foreach (var pair in Moved.Take(limit)) yield return $"moved from {pair.Key.Where}: {pair.Value}";
        }
    }

    /// <summary>Compares the items on the player in a save with the items on the restored profile, by id.</summary>
    public static class InventoryCheck
    {
        public static InventoryDiff Compare(IEnumerable<ItemRecord> saved, IEnumerable<ItemRecord> now)
        {
            var diff = new InventoryDiff();
            var before = saved.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var after = now.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            diff.Saved = before.Count;
            diff.Now = after.Count;
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var current))
                {
                    diff.Missing.Add(pair.Value);
                    continue;
                }
                if (current.FoundInRaid != pair.Value.FoundInRaid)
                {
                    var changed = new KeyValuePair<ItemRecord, ItemRecord>(pair.Value, current);
                    (current.IsAmmo || pair.Value.IsAmmo ? diff.AmmoFoundInRaidChanged : diff.FoundInRaidChanged).Add(changed);
                }
                if (!string.Equals(current.Where, pair.Value.Where, StringComparison.Ordinal))
                {
                    diff.Moved.Add(new KeyValuePair<ItemRecord, ItemRecord>(pair.Value, current));
                }
            }
            foreach (var pair in after)
            {
                if (!before.ContainsKey(pair.Key))
                {
                    diff.Extra.Add(pair.Value);
                }
            }
            return diff;
        }
    }
}
