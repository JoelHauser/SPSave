using System;

namespace SPSave.Core
{
    /// <summary>Numbers from one capture, logged as one line (Summary) after each save.</summary>
    public sealed class CaptureReport
    {
        public DateTime Utc;
        public string Map = "";
        public string ProfileId = "";
        public int BotsAlive;
        public int SkippedBots;
        public int Corpses;
        public int LooseItems;
        public int Containers;
        public int SkippedLoot;
        public string SkippedLootKinds = "";
        public int WorldItemIds;
        public int PlayerItemIds;
        /// <summary>Item ids found in more than one place (DuplicateCheck); 0 when the save is clean.</summary>
        public int DuplicateItems;
        public int Doors;
        public int PlayerEffects;
        public string Weather = "";
        public bool InCombat;
        public string CombatDetail = "";
        public long RawBytes;
        public long FileBytes;
        public double PlayerMs;
        public double BotsMs;
        public double LootMs;
        public double WorldMs;
        public double StateMs;
        /// <summary>Everything that ran on the main thread in the capture frame, file I/O included.</summary>
        public double FrameMs;
        public double EncodeMs;
        public double WriteMs;
        public double VerifyFileMs;
        public double RoundTripMs;
        public bool RoundTripOk;
        public string RoundTripDetail = "";

        public string Summary()
        {
            return $"{Map}: frame {FrameMs:F0} ms (player {PlayerMs:F0}, bots {BotsMs:F0}, loot {LootMs:F0}, world {WorldMs:F0}, " +
                   $"state {StateMs:F0}, encode {EncodeMs:F0}, write {WriteMs:F0}, verify {VerifyFileMs:F0}, round trip {RoundTripMs:F0}); " +
                   $"{BotsAlive} bots, {Corpses} corpses, {LooseItems} loose, {Containers} containers, {Doors} doors; " +
                   $"{(DuplicateItems == 0 ? "no duplicate items" : $"{DuplicateItems} DUPLICATE ITEMS")}; weather {Weather}; " +
                   $"{FileBytes / 1024} KB on disk ({RawBytes / 1024} KB raw); {CombatDetail}; round trip {(RoundTripOk ? "ok" : "FAILED")}";
        }
    }
}
