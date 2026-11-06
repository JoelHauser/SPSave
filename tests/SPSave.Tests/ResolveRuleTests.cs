using SPSave.Core;

namespace SPSave.Tests
{
    public static class ResolveRuleTests
    {
        public static void PlainSaveResumes()
        {
            Assert.Equal(null, ResolveRule.Choose(null, null), "no outcome");
        }

        public static void AbandonEndsAsMia()
        {
            Assert.Equal(ResolveOutcome.MissingInAction, ResolveRule.Choose(null, ResolveOutcome.MissingInAction), "abandon");
        }

        public static void CombatSaveEndsAsKilled()
        {
            Assert.Equal(ResolveOutcome.Killed, ResolveRule.Choose(ResolveRule.SavedInCombat, null), "combat save");
        }

        public static void CombatSaveStaysKilledWhenAbandoned()
        {
            Assert.Equal(ResolveOutcome.Killed, ResolveRule.Choose(ResolveRule.SavedInCombat, ResolveOutcome.MissingInAction), "combat save abandoned");
        }

        public static void UnknownSavedOutcomeIsRefused()
        {
            Assert.Throws<System.InvalidOperationException>(() => ResolveRule.Choose("Survived", null), "unknown outcome");
        }
    }
}
