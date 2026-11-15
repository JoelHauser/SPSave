using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;

namespace SPSave.Game
{
    /// <summary>
    /// Found-in-raid ammo. In the game, <c>Ammo.SpawnedInSession</c> always reads false and ignores writes, so ammo is
    /// never found in raid. Actually Found In Raid (com.rukiragaming.actuallyfir) patches the getter and setter to
    /// keep the flag in a table keyed by the live Ammo object; a profile or loot rebuilt from a save has new Ammo
    /// objects, which read false (issue #1: every save failed its check on two rounds). The save lists those rounds'
    /// ids (<see cref="RaidState.FoundInRaidAmmo"/>) and every rebuild sets them again through the setter, which
    /// that mod patches. Without the mod the list is empty and nothing changes.
    /// </summary>
    internal static class AmmoFoundInRaid
    {
        /// <summary>Adds the ids of found-in-raid ammo among <paramref name="items"/> to <paramref name="ids"/>.</summary>
        internal static void Collect(IEnumerable<Item> items, HashSet<string> ids)
        {
            foreach (var item in items)
            {
                if (item is Ammo && item.SpawnedInSession)
                {
                    ids.Add(item.Id);
                }
            }
        }

        /// <summary>
        /// Marks the listed ammo among <paramref name="items"/> found in raid again. Returns how many were set and how
        /// many still read false afterwards (a setter nothing patches).
        /// </summary>
        internal static (int Set, int Refused) Apply(IEnumerable<Item> items, HashSet<string> ids)
        {
            var set = 0;
            var refused = 0;
            if (ids == null || ids.Count == 0)
            {
                return (0, 0);
            }
            foreach (var item in items)
            {
                if (!(item is Ammo) || item.SpawnedInSession || !ids.Contains(item.Id))
                {
                    continue;
                }
                item.SpawnedInSession = true;
                if (item.SpawnedInSession)
                {
                    set++;
                }
                else
                {
                    refused++;
                }
            }
            return (set, refused);
        }

        internal static HashSet<string> Ids(RaidState state)
        {
            return state?.FoundInRaidAmmo == null || state.FoundInRaidAmmo.Count == 0
                ? null
                : new HashSet<string>(state.FoundInRaidAmmo, StringComparer.Ordinal);
        }

        internal static IEnumerable<Item> ItemsOf(Profile profile)
        {
            return profile?.Inventory?.GetPlayerItems(EPlayerItems.All) ?? Enumerable.Empty<Item>();
        }

        /// <summary>Every item in world loot entries, nested contents included.</summary>
        internal static IEnumerable<Item> ItemsOf(IEnumerable<JsonType.JsonLootItem> loot)
        {
            foreach (var entry in loot)
            {
                if (entry.Item is ContainerCollection collection)
                {
                    foreach (var item in collection.GetAllItemsFromCollection())
                    {
                        yield return item;
                    }
                }
                else if (entry.Item != null)
                {
                    yield return entry.Item;
                }
            }
        }

        internal static void Log(string when, (int Set, int Refused) result)
        {
            if (result.Set > 0)
            {
                Plugin.Log.LogInfo($"{when}: {result.Set} found-in-raid rounds of ammo marked again.");
            }
            if (result.Refused > 0)
            {
                Plugin.Log.LogWarning($"{when}: {result.Refused} rounds of ammo saved as found in raid can't be marked again; they come back not found in raid.");
            }
        }
    }
}
