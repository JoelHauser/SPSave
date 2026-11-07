using System;
using System.Collections.Generic;
using System.Linq;

namespace SPSave.Core
{
    /// <summary>Plugins that differ between a save and the game now. Entries are "guid@version", as the manifest stores them.</summary>
    public sealed class ModListDiff
    {
        public readonly List<string> Missing = new List<string>();
        /// <summary>"guid saved-version → current-version".</summary>
        public readonly List<string> Changed = new List<string>();
        public readonly List<string> Added = new List<string>();

        /// <summary>A missing or changed mod can break a resume (its items, its bots); an added one rarely does.</summary>
        public bool NeedsConfirmation => Missing.Count > 0 || Changed.Count > 0;

        public string Describe(int limit = 6)
        {
            var lines = Missing.Select(m => m + " (missing)").Concat(Changed).ToList();
            var shown = string.Join(", ", lines.Take(limit));
            return lines.Count > limit ? $"{shown} and {lines.Count - limit} more" : shown;
        }
    }

    public static class ModListCheck
    {
        /// <param name="ignore">GUIDs that never matter, e.g. SPSave itself (its version changes are checked separately).</param>
        public static ModListDiff Compare(IEnumerable<string> saved, IEnumerable<string> current, ICollection<string> ignore = null)
        {
            var before = Parse(saved, ignore);
            var now = Parse(current, ignore);
            var diff = new ModListDiff();
            foreach (var pair in before.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!now.TryGetValue(pair.Key, out var version))
                {
                    diff.Missing.Add(pair.Key);
                }
                else if (!string.Equals(version, pair.Value, StringComparison.Ordinal))
                {
                    diff.Changed.Add($"{pair.Key} {pair.Value} → {version}");
                }
            }
            foreach (var key in now.Keys.Where(k => !before.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
            {
                diff.Added.Add(key);
            }
            return diff;
        }

        private static Dictionary<string, string> Parse(IEnumerable<string> entries, ICollection<string> ignore)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }
                var at = entry.LastIndexOf('@');
                var guid = at > 0 ? entry.Substring(0, at) : entry;
                var version = at > 0 ? entry.Substring(at + 1) : "";
                if (ignore != null && ignore.Contains(guid))
                {
                    continue;
                }
                result[guid] = version;
            }
            return result;
        }
    }
}
