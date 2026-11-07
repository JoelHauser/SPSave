using System.Collections.Generic;
using SPSave.Core;

namespace SPSave.Tests
{
    public static class DuplicateCheckTests
    {
        private static KeyValuePair<string, IEnumerable<string>> Owner(string name, params string[] ids) =>
            new KeyValuePair<string, IEnumerable<string>>(name, ids);

        public static void DistinctOwnersAreClean()
        {
            var d = DuplicateCheck.Find(new[] { Owner("player", "a", "b"), Owner("bot 1", "c"), Owner("world", "d") });
            Assert.Equal(0, d.Count, "no duplicates");
        }

        public static void AnIdInTwoPlacesIsReportedWithBoth()
        {
            var d = DuplicateCheck.Find(new[] { Owner("player", "a", "gun"), Owner("world", "gun", "x") });
            Assert.Equal(1, d.Count, "one duplicate");
            Assert.True(d["gun"].Contains("player") && d["gun"].Contains("world"), "both owners named");
        }

        public static void RepeatsWithinOneOwnerDontCount()
        {
            // GetPlayerItems can yield an item twice; that's one place.
            Assert.Equal(0, DuplicateCheck.Find(new[] { Owner("player", "a", "a"), Owner("world", "b") }).Count, "same owner");
        }
    }
}
