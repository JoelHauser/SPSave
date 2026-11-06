using System.Linq;
using SPSave.Core;

namespace SPSave.Tests
{
    public static class InventoryCheckTests
    {
        private static ItemRecord Item(string id, bool fir = false, string where = "main of rig") =>
            new ItemRecord { Id = id, Template = "tpl-" + id, FoundInRaid = fir, Where = where };

        public static void SameItemsAreClean()
        {
            var diff = InventoryCheck.Compare(new[] { Item("a", true), Item("b") }, new[] { Item("b"), Item("a", true) });
            Assert.True(diff.Clean, diff.Summary());
        }

        public static void FindsMissingExtraFlagAndPlace()
        {
            var diff = InventoryCheck.Compare(
                new[] { Item("a", true), Item("b", true), Item("c"), Item("d") },
                new[] { Item("a", true), Item("b", false), Item("c", where: "1 of vest"), Item("e") });
            Assert.Equal(1, diff.Missing.Count, "missing");
            Assert.Equal("d", diff.Missing[0].Id, "missing id");
            Assert.Equal(1, diff.Extra.Count, "extra");
            Assert.Equal(1, diff.FoundInRaidChanged.Count, "found-in-raid changed");
            Assert.Equal(1, diff.Moved.Count, "moved");
            Assert.Equal(4, diff.Details(10).Count(), "one detail line each");
        }
    }
}
