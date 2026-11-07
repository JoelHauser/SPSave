using System;
using System.Collections.Generic;
using System.Linq;

namespace SPSave.Core
{
    /// <summary>
    /// Every item id in a save must have exactly one place: on the player, on one bot, or in the world. An id in two
    /// places is a duplicated item, which a resume would bring back twice (and extraction would keep).
    /// </summary>
    public static class DuplicateCheck
    {
        /// <summary>Ids found under more than one owner, each with the owners it was found under.</summary>
        public static Dictionary<string, List<string>> Find(IEnumerable<KeyValuePair<string, IEnumerable<string>>> owners)
        {
            var seen = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var owner in owners)
            {
                foreach (var id in (owner.Value ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal))
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }
                    if (!seen.TryGetValue(id, out var list))
                    {
                        seen.Add(id, list = new List<string>());
                    }
                    list.Add(owner.Key);
                }
            }
            return seen.Where(p => p.Value.Count > 1).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }
    }
}
