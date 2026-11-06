using System.IO;
using System.Linq;
using SPSave.Core;

namespace SPSave.Tests
{
    public static class CaptureReportTests
    {
        public static void StartsNewFileWhenColumnsChange()
        {
            using var dir = new TempDir();
            var csv = dir.File("captures.csv");
            File.WriteAllText(csv, "utc,map,old_columns\nrow\n");

            CaptureReport.AppendTo(csv, new CaptureReport { Map = "bigmap" });

            var lines = File.ReadAllLines(csv);
            Assert.Equal(CaptureReport.CsvHeader, lines[0], "new header");
            Assert.Equal(2, lines.Length, "header and one row");
            Assert.Equal(2, Directory.GetFiles(dir.Path, "captures.*.csv").Length + 1, "old file kept beside it");
        }

        public static void AppendsUnderMatchingHeader()
        {
            using var dir = new TempDir();
            var csv = dir.File("captures.csv");
            CaptureReport.AppendTo(csv, new CaptureReport { Map = "a" });
            CaptureReport.AppendTo(csv, new CaptureReport { Map = "b" });
            Assert.Equal(3, File.ReadAllLines(csv).Length, "header and two rows");
            Assert.True(!Directory.GetFiles(dir.Path, "captures.*.csv").Any(), "nothing set aside");
        }

        public static void RowHasAColumnPerHeader()
        {
            var row = new CaptureReport { CombatDetail = "in combat (a, b)", SkippedLootKinds = "X x1; Y x2" }.ToCsvRow();
            // Quoted fields may hold commas; count columns outside quotes.
            int columns = 1;
            var quoted = false;
            foreach (var ch in row)
            {
                if (ch == '"') quoted = !quoted;
                else if (ch == ',' && !quoted) columns++;
            }
            Assert.Equal(CaptureReport.CsvHeader.Split(',').Length, columns, "columns");
        }
    }
}
