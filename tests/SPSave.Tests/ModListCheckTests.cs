using SPSave.Core;

namespace SPSave.Tests
{
    public static class ModListCheckTests
    {
        public static void SameModsNeedNothing()
        {
            var diff = ModListCheck.Compare(new[] { "a@1.0", "b@2.0" }, new[] { "b@2.0", "a@1.0" });
            Assert.True(!diff.NeedsConfirmation && diff.Added.Count == 0, "identical lists");
        }

        public static void MissingModNeedsConfirmation()
        {
            var diff = ModListCheck.Compare(new[] { "a@1.0", "b@2.0" }, new[] { "a@1.0" });
            Assert.True(diff.NeedsConfirmation, "missing mod");
            Assert.Equal("b", diff.Missing[0], "missing guid");
        }

        public static void ChangedVersionNeedsConfirmation()
        {
            var diff = ModListCheck.Compare(new[] { "a@1.0" }, new[] { "a@1.1" });
            Assert.True(diff.NeedsConfirmation, "changed version");
            Assert.Equal("a 1.0 → 1.1", diff.Changed[0], "change text");
        }

        public static void AddedModIsOnlyReported()
        {
            var diff = ModListCheck.Compare(new[] { "a@1.0" }, new[] { "a@1.0", "c@3.0" });
            Assert.True(!diff.NeedsConfirmation, "added mod alone");
            Assert.Equal("c", diff.Added[0], "added guid");
        }

        public static void IgnoredGuidsDontCount()
        {
            var diff = ModListCheck.Compare(new[] { "self@0.7.0", "a@1.0" }, new[] { "self@0.8.0", "a@1.0" }, new[] { "self" });
            Assert.True(!diff.NeedsConfirmation, "own version change ignored");
        }

        public static void GuidsWithAtSignsSplitAtTheLastOne()
        {
            var diff = ModListCheck.Compare(new[] { "x@y@1.0" }, new[] { "x@y@1.0" });
            Assert.True(!diff.NeedsConfirmation && diff.Added.Count == 0, "guid containing @");
        }

        public static void LongListsAreShortened()
        {
            var diff = ModListCheck.Compare(new[] { "a@1", "b@1", "c@1", "d@1" }, new string[0]);
            Assert.Equal("a (missing), b (missing) and 2 more", diff.Describe(2), "describe");
        }
    }
}
