using SPSave.Core;

namespace SPSave.Tests
{
    public static class EffectTimingTests
    {
        public static void StartedEffectKeepsItsRemainingTime()
        {
            var t = EffectTiming.For("Started", timeLeft: 95f, workStateTime: 300f);
            Assert.True(t.Restore, "restored");
            Assert.Equal(0f, t.Delay.Value, "no delay");
            Assert.Equal(95f, t.Work.Value, "work time left");
        }

        public static void EndlessEffectUsesTheDefault()
        {
            var t = EffectTiming.For("Started", float.PositiveInfinity, float.PositiveInfinity);
            Assert.True(t.Restore && t.Work == null, "endless work stays default");
        }

        public static void DelayedEffectKeepsDelayLeftAndFullWork()
        {
            // 4 s of delay left, then 60 s of work.
            var t = EffectTiming.For("Added", timeLeft: 64f, workStateTime: 60f);
            Assert.Equal(4f, t.Delay.Value, "delay left");
            Assert.Equal(60f, t.Work.Value, "full work");
        }

        public static void EndingEffectsAreNotRestored()
        {
            Assert.True(!EffectTiming.For("Residued", 3f, 60f).Restore, "residued");
            Assert.True(!EffectTiming.For("Removed", 0f, 60f).Restore, "removed");
        }
    }
}
