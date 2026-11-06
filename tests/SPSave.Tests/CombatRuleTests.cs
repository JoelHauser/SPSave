using SPSave.Core;

namespace SPSave.Tests
{
    public static class CombatRuleTests
    {
        private const float Window = 30f;

        public static void NoBotsIsNotCombat()
        {
            Assert.True(!CombatRule.Evaluate(new BotAwareness[0], Window).InCombat, "empty raid");
        }

        public static void BotThatDoesNotKnowYouIsIgnored()
        {
            var bot = new BotAwareness { Knows = false, Visible = true, SecondsSinceSeen = 1f };
            Assert.True(!CombatRule.Evaluate(new[] { bot }, Window).InCombat, "unknown enemy entries are ignored");
        }

        public static void VisibleNowIsCombat()
        {
            var verdict = CombatRule.Evaluate(new[] { new BotAwareness { Knows = true, Visible = true } }, Window);
            Assert.True(verdict.InCombat, "visible");
            Assert.Equal(1, verdict.Visible, "visible count");
        }

        public static void SeenOrShotWithinWindowIsCombat()
        {
            Assert.True(CombatRule.Evaluate(new[] { new BotAwareness { Knows = true, SecondsSinceSeen = 29f } }, Window).InCombat, "seen 29 s ago");
            Assert.True(CombatRule.Evaluate(new[] { new BotAwareness { Knows = true, SecondsSinceShot = 2f } }, Window).InCombat, "shot at 2 s ago");
            Assert.True(CombatRule.Evaluate(new[] { new BotAwareness { Knows = true, SecondsSinceSeen = 30f } }, Window).InCombat, "window is inclusive");
        }

        public static void OldContactIsNotCombat()
        {
            var verdict = CombatRule.Evaluate(new[] { new BotAwareness { Knows = true, SecondsSinceSeen = 120f, SecondsSinceShot = 118f } }, Window);
            Assert.True(!verdict.InCombat, "two minutes ago");
            Assert.Equal(1, verdict.NoContact, "counted as no contact");
        }

        public static void HostileWithoutContactIsNotCombat()
        {
            // Every hostile bot has an enemy entry from spawn; one hunting you by sound has no contact either.
            var verdict = CombatRule.Evaluate(new[] { new BotAwareness { Knows = true } }, Window);
            Assert.True(!verdict.InCombat, "no contact");
            Assert.Equal(1, verdict.NoContact, "no-contact count");
        }

        public static void ShooterDeadIsNotCombat()
        {
            // Customs, 2026-11-06: the only bot in contact was killed; the dead are not in the list, so no combat.
            var survivors = new[] { new BotAwareness { Knows = true }, new BotAwareness { Knows = true } };
            Assert.True(!CombatRule.Evaluate(survivors, Window).InCombat, "attacker dead");
        }

        public static void OneBotInContactIsEnough()
        {
            var bots = new[]
            {
                new BotAwareness { Knows = true },
                new BotAwareness { Knows = false },
                new BotAwareness { Knows = true, SecondsSinceShot = 5f }
            };
            var verdict = CombatRule.Evaluate(bots, Window);
            Assert.True(verdict.InCombat, "one of three");
            Assert.Equal(1, verdict.ShotRecently, "shot count");
            Assert.Equal(1, verdict.NoContact, "no-contact count");
        }
    }
}
