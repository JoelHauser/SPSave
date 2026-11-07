using System;

namespace SPSave.Core
{
    /// <summary>How a save is ended instead of resumed. Names match EFT's ExitStatus.</summary>
    public enum ResolveOutcome
    {
        Killed,
        MissingInAction
    }

    /// <summary>
    /// Whether loading a save resumes it or ends it, and how (Joel, 2026-11-06): a save made in combat always ends as
    /// Killed, even if the player chose Abandon; Abandon ends as MIA; otherwise the raid resumes.
    /// </summary>
    public static class ResolveRule
    {
        public const string SavedInCombat = "Killed";

        /// <param name="manifestResolveAs">The save's own outcome: <see cref="SavedInCombat"/> or null.</param>
        /// <param name="requested">The menu's request (Abandon is MissingInAction), or null.</param>
        public static ResolveOutcome? Choose(string manifestResolveAs, ResolveOutcome? requested)
        {
            if (string.Equals(manifestResolveAs, SavedInCombat, StringComparison.Ordinal))
            {
                return ResolveOutcome.Killed;
            }
            if (!string.IsNullOrEmpty(manifestResolveAs))
            {
                // A newer or damaged manifest: ending a raid is not reversible, so never guess.
                throw new InvalidOperationException($"The save asks to end as '{manifestResolveAs}', which this version doesn't know.");
            }
            return requested;
        }
    }
}
