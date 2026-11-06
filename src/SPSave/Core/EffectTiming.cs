namespace SPSave.Core
{
    /// <summary>The delay and work time to re-add a saved health effect with, so it carries on where it was.</summary>
    public struct EffectTimes
    {
        public bool Restore;
        /// <summary>Null means the effect's default.</summary>
        public float? Delay;
        /// <summary>Null means the effect's default (often endless).</summary>
        public float? Work;
    }

    /// <summary>
    /// ActiveHealthController.Effect.TimeLeft is DelayTime + WorkStateTime minus the time the effect has existed
    /// (outside its residue). So for a started effect it is the work time left; for one still in its delay ("Added")
    /// it is the delay left plus the whole work time. Residued and removed effects are ending and are not restored.
    /// </summary>
    public static class EffectTiming
    {
        public static EffectTimes For(string state, float timeLeft, float workStateTime)
        {
            switch (state)
            {
                case "Started":
                    return new EffectTimes { Restore = true, Delay = 0f, Work = Finite(timeLeft) };
                case "Added":
                {
                    var work = Finite(workStateTime);
                    float? delay = null;
                    if (work.HasValue && Finite(timeLeft).HasValue)
                    {
                        delay = timeLeft - work.Value > 0f ? timeLeft - work.Value : 0f;
                    }
                    return new EffectTimes { Restore = true, Delay = delay, Work = work };
                }
                default:
                    return new EffectTimes { Restore = false };
            }
        }

        private static float? Finite(float value)
        {
            return float.IsInfinity(value) || float.IsNaN(value) || value <= 0f ? (float?)null : value;
        }
    }
}
