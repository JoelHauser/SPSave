using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EFT;
using EFT.BinarySerialization;
using JsonType;
using Mirror;

namespace SPSave.Game
{
    /// <summary>
    /// The world's loot in a save. Written as JSON through the game's LootItemSerializer, the format the SPT server
    /// sends raid loot in: flat items that name their container (FlatItem.slotId), rebuilt with
    /// ItemFactory.FlatItemsToTree. It deserializes records with a customization as JsonCorpse, so one list holds both.
    ///
    /// Not the binary ItemDescriptor format: that stores a grid by its index in the item's container list
    /// (GridDescriptor.GridNumber), and an item whose live instance has containers its template lacks (a rig on a
    /// corpse in Joel's game had two) lands in the wrong grid on read-back; Grid.AddItemWithoutRestrictions then
    /// parks it outside the grid and it is lost. Saves from 0.2.0 builds before this change still read through it.
    /// </summary>
    internal static class WorldLoot
    {
        public const string JsonSection = "world.loot.json";
        public const string LegacyBinarySection = "world.loot";

        public static KeyValuePair<string, byte[]> Write(List<JsonLootItem> loot)
        {
            return new KeyValuePair<string, byte[]>(JsonSection, Encoding.UTF8.GetBytes(loot.ToArray().ToJson()));
        }

        public static LootData Read(Dictionary<string, byte[]> sections)
        {
            if (sections.TryGetValue(JsonSection, out var json))
            {
                var entries = Encoding.UTF8.GetString(json).ParseJsonTo<JsonLootItem[]>();
                return new LootData(entries?.ToList() ?? new List<JsonLootItem>());
            }
            if (sections.TryGetValue(LegacyBinarySection, out var binary))
            {
                var descriptor = new NetworkReader(new ArraySegment<byte>(binary)).ReadEFTLootDataDescriptor();
                return ItemBinarySerializer.DeserializeLootData(descriptor);
            }
            throw new InvalidOperationException("Save has no world loot.");
        }

        public static bool Has(Dictionary<string, byte[]> sections)
        {
            return sections.ContainsKey(JsonSection) || sections.ContainsKey(LegacyBinarySection);
        }
    }
}
