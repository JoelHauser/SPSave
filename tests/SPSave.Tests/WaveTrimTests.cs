using SPSave.Core;

namespace SPSave.Tests
{
    public static class WaveTrimTests
    {
        public static void LaterBossWaveKeepsWhatWasLeft()
        {
            // ABPS PMC wave at 720 s, saved 300 s after the scenario ran: 420 s to go.
            Assert.Equal(420f, WaveTrim.Boss(720f, false, true, false, 300f).Value, "time left");
        }

        public static void FiredOrFailedBossWavesAreDropped()
        {
            Assert.True(WaveTrim.Boss(120f, false, true, activated: true, 300f) == null, "activated");
            Assert.True(WaveTrim.Boss(720f, false, shallSpawn: false, false, 300f) == null, "chance roll failed: still won't spawn");
        }

        public static void StartWavesAreDropped()
        {
            Assert.True(WaveTrim.Boss(-1f, false, true, false, 300f) == null, "start wave (starting PMCs)");
            Assert.True(WaveTrim.Boss(0f, false, true, false, 300f) == null, "Time 0 never fires");
        }

        public static void TriggerWavesKeepWaitingForTheirTrigger()
        {
            Assert.Equal(-1f, WaveTrim.Boss(-1f, true, true, false, 300f).Value, "trigger only");
            Assert.Equal(600f, WaveTrim.Boss(900f, true, true, false, 300f).Value, "trigger with a timer");
        }

        public static void DueButUnfiredBossWaveFiresSoon()
        {
            Assert.Equal(WaveTrim.MinDelay, WaveTrim.Boss(299.5f, false, true, false, 300f).Value, "overdue");
        }

        public static void OldWavesBeforeTheSaveAreDropped()
        {
            Assert.True(WaveTrim.Old(-1f, 300f) == null, "spawned before the game started");
            Assert.True(WaveTrim.Old(120f, 300f) == null, "fired");
            Assert.True(WaveTrim.Old(300f, 300f) == null, "fired on the save's frame");
            Assert.Equal(60f, WaveTrim.Old(360f, 300f).Value, "60 s to go");
        }
    }
}
