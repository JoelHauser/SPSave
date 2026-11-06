using System.Collections.Generic;

namespace SPSave.Core
{
    /// <summary>
    /// One living bot's view of the player at the moment of saving. <see cref="Knows"/> only means the bot has an
    /// enemy entry for the player; in game every hostile bot has one from spawn, contact or not.
    /// </summary>
    public struct BotAwareness
    {
        public bool Knows;
        public bool Visible;
        /// <summary>Seconds since the bot last saw the player; null if it never has.</summary>
        public float? SecondsSinceSeen;
        /// <summary>Seconds since the bot last shot at the player; null if it never has.</summary>
        public float? SecondsSinceShot;
    }

    public sealed class CombatVerdict
    {
        public bool InCombat;
        public int Visible;
        public int SeenRecently;
        public int ShotRecently;
        /// <summary>Bots with an enemy entry for the player but no contact within the window.</summary>
        public int NoContact;

        public string Describe()
        {
            return InCombat
                ? $"in combat (visible to {Visible}, seen recently by {SeenRecently}, shot at recently by {ShotRecently}; {NoContact} hostile without contact)"
                : $"not in combat ({NoContact} hostile without contact)";
        }
    }

    /// <summary>
    /// Joel's rule: saving while in combat resolves the raid as Killed. "In combat" means a living bot can see the
    /// player now, or saw or shot at them within the window. A hostile bot without contact does not count, even if
    /// it is hunting the player from a sound or a squadmate's report. Evaluated once, at save time.
    /// </summary>
    public static class CombatRule
    {
        public static CombatVerdict Evaluate(IEnumerable<BotAwareness> bots, float windowSeconds)
        {
            var verdict = new CombatVerdict();
            foreach (var bot in bots)
            {
                if (!bot.Knows)
                {
                    continue;
                }
                var seen = bot.SecondsSinceSeen.HasValue && bot.SecondsSinceSeen.Value <= windowSeconds;
                var shot = bot.SecondsSinceShot.HasValue && bot.SecondsSinceShot.Value <= windowSeconds;
                if (bot.Visible)
                {
                    verdict.Visible++;
                }
                if (seen)
                {
                    verdict.SeenRecently++;
                }
                if (shot)
                {
                    verdict.ShotRecently++;
                }
                if (!bot.Visible && !seen && !shot)
                {
                    verdict.NoContact++;
                }
            }
            verdict.InCombat = verdict.Visible + verdict.SeenRecently + verdict.ShotRecently > 0;
            return verdict;
        }
    }
}
