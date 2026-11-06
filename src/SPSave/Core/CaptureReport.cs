using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SPSave.Core
{
    /// <summary>Numbers from one capture, appended as a row to captures.csv.</summary>
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

        public const string CsvHeader =
            "utc,map,profile,bots_alive,skipped_bots,corpses,loose_items,containers,skipped_loot,skipped_loot_kinds,world_item_ids,player_item_ids,doors,player_effects,weather," +
            "in_combat,combat_detail,raw_bytes,file_bytes,player_ms,bots_ms,loot_ms,world_ms,state_ms,encode_ms,write_ms,verify_file_ms," +
            "round_trip_ms,frame_ms,round_trip_ok,round_trip_detail";

        public string ToCsvRow()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(",",
                Utc.ToString("o", c), Csv(Map), Csv(ProfileId),
                BotsAlive.ToString(c), SkippedBots.ToString(c), Corpses.ToString(c), LooseItems.ToString(c), Containers.ToString(c), SkippedLoot.ToString(c), Csv(SkippedLootKinds),
                WorldItemIds.ToString(c), PlayerItemIds.ToString(c), Doors.ToString(c), PlayerEffects.ToString(c), Csv(Weather),
                InCombat ? "1" : "0", Csv(CombatDetail), RawBytes.ToString(c), FileBytes.ToString(c),
                Ms(PlayerMs), Ms(BotsMs), Ms(LootMs), Ms(WorldMs), Ms(StateMs), Ms(EncodeMs), Ms(WriteMs), Ms(VerifyFileMs),
                Ms(RoundTripMs), Ms(FrameMs), RoundTripOk ? "1" : "0", Csv(RoundTripDetail));
        }

        public string Summary()
        {
            return $"{Map}: frame {FrameMs:F0} ms (player {PlayerMs:F0}, bots {BotsMs:F0}, loot {LootMs:F0}, world {WorldMs:F0}, " +
                   $"state {StateMs:F0}, encode {EncodeMs:F0}, write {WriteMs:F0}, verify {VerifyFileMs:F0}, round trip {RoundTripMs:F0}); " +
                   $"{BotsAlive} bots, {Corpses} corpses, {LooseItems} loose, {Containers} containers, {Doors} doors; " +
                   $"{(DuplicateItems == 0 ? "no duplicate items" : $"{DuplicateItems} DUPLICATE ITEMS")}; weather {Weather}; " +
                   $"{FileBytes / 1024} KB on disk ({RawBytes / 1024} KB raw); {CombatDetail}; round trip {(RoundTripOk ? "ok" : "FAILED")}";
        }

        public static void AppendTo(string csvPath, CaptureReport report)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(csvPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            // A file written by an older build has different columns: keep it, but start a new one.
            if (File.Exists(csvPath))
            {
                string header;
                using (var reader = new StreamReader(csvPath))
                {
                    header = reader.ReadLine();
                }
                if (header != CsvHeader)
                {
                    var old = Path.Combine(directory ?? "", Path.GetFileNameWithoutExtension(csvPath) + "." +
                        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Path.GetExtension(csvPath));
                    File.Move(csvPath, old);
                }
            }

            var builder = new StringBuilder();
            if (!File.Exists(csvPath))
            {
                builder.AppendLine(CsvHeader);
            }
            builder.AppendLine(report.ToCsvRow());
            File.AppendAllText(csvPath, builder.ToString());
        }

        private static string Ms(double value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
