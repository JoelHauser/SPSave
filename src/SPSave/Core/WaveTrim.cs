namespace SPSave.Core
{
    /// <summary>
    /// Which saved spawn waves a resume keeps, and when they fire. Wave timers count Time.time from their scenario's
    /// Run (TimerManager.MakeTimer), so "elapsed" is the Time.time between Run and the save, not the raid timer.
    /// A resume runs the scenario again, so a kept wave's new time is what was left of it.
    ///
    /// Boss waves (BossSpawnScenario, which also carries ABPS's PMCs): Run starts a timer for Time &gt; 0, fires
    /// Time &lt; 0 at once (start waves), and never times a wave with Time 0; trigger waves also wait for their
    /// trigger. A wave fires only if its chance roll (ShallSpawn) passed, and marks itself Activated when it does.
    ///
    /// Old-style waves (WavesSpawnScenario.SpawnWaves) have no Activated flag: Time &lt; 0 spawned before the game
    /// started, the rest fired once Time had elapsed.
    /// </summary>
    public static class WaveTrim
    {
        /// <summary>A wave whose timer was due but hadn't fired still waits this long, so nothing fires mid-load.</summary>
        public const float MinDelay = 1f;

        /// <summary>The boss wave's new Time, or null to drop it.</summary>
        public static float? Boss(float time, bool trigger, bool shallSpawn, bool activated, float elapsed)
        {
            if (!shallSpawn || activated)
            {
                return null;
            }
            if (time <= 0f)
            {
                // Start waves fired when the raid began; Time 0 never fires on a timer. A trigger wave still waits
                // for its trigger either way.
                return trigger ? time : (float?)null;
            }
            var left = time - elapsed;
            return left > MinDelay ? left : MinDelay;
        }

        /// <summary>The old-style wave's new Time, or null to drop it (it already spawned).</summary>
        public static float? Old(float time, float elapsed)
        {
            if (time < 0f || time <= elapsed)
            {
                return null;
            }
            return time - elapsed;
        }
    }
}
