using System;
using System.Text;
using EFT;
using EFT.BinarySerialization;
using Mirror;

namespace SPSave.Game
{
    /// <summary>
    /// Profiles in a save. Written as JSON (ProfileDescriptor.ToJson), the form SPT sends the server at every raid end:
    /// InventoryDescriptor.OnJSONSerializing flattens the inventory into FlatItem[] that name their container, and
    /// ToInventory rebuilds it with ItemFactory.FlatItemsToTree.
    ///
    /// Not the binary form (WriteEFTProfileDescriptor): it carries the inventory as an ItemDescriptor tree whose grids
    /// are found by index, and ToInventory prefers that tree when present. Joel's rig had one container its template
    /// lacks, so on rebuild every grid's contents moved down one and the first grid's fell out. Section names ending in
    /// ".json" are JSON; older saves' binary sections still read.
    /// </summary>
    internal static class ProfileCodec
    {
        public const string JsonSuffix = ".json";

        public static byte[] Write(ProfileDescriptor descriptor)
        {
            return Encoding.UTF8.GetBytes(descriptor.ToJson());
        }

        public static ProfileDescriptor Read(string sectionName, byte[] bytes)
        {
            if (sectionName.EndsWith(JsonSuffix, StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetString(bytes).ParseJsonTo<ProfileDescriptor>()
                       ?? throw new InvalidOperationException($"{sectionName} is empty.");
            }
            return new NetworkReader(new ArraySegment<byte>(bytes)).ReadEFTProfileDescriptor();
        }

        public static bool IsProfileSection(string name)
        {
            return name.StartsWith(RaidCapture.PlayerSection, StringComparison.Ordinal)
                   || name.StartsWith(RaidCapture.BotSectionPrefix, StringComparison.Ordinal);
        }
    }
}
