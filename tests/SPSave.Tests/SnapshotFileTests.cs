using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SPSave.Core;

namespace SPSave.Tests
{
    public static class SnapshotFileTests
    {
        private static List<KeyValuePair<string, byte[]>> Sections(string tag)
        {
            var random = new Random(tag.GetHashCode());
            var blob = new byte[200_000];
            random.NextBytes(blob);
            return new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("manifest.json", Encoding.UTF8.GetBytes("{\"tag\":\"" + tag + "\"}")),
                new KeyValuePair<string, byte[]>("player.profile", blob),
                new KeyValuePair<string, byte[]>("bot/abc", Array.Empty<byte>()),
                new KeyValuePair<string, byte[]>("world.loot", Enumerable.Repeat((byte)7, 50_000).ToArray())
            };
        }

        private static string Tag(Dictionary<string, byte[]> sections)
        {
            return Encoding.UTF8.GetString(sections["manifest.json"]);
        }

        public static void RoundTripsEverySection()
        {
            var sections = Sections("a");
            var decoded = SnapshotFile.Decode(SnapshotFile.Encode(sections));
            Assert.Equal(sections.Count, decoded.Count, "section count");
            foreach (var section in sections)
            {
                Assert.True(decoded[section.Key].SequenceEqual(section.Value), $"section {section.Key} differs");
            }
        }

        public static void RejectsAnyFlippedByte()
        {
            var file = SnapshotFile.Encode(Sections("a"));
            // Header (magic, version, hash, length) and a spread of body offsets.
            var offsets = new[] { 0, 6, 7, 20, 39, 40, 47, 48, file.Length / 2, file.Length - 1 };
            foreach (var offset in offsets)
            {
                var damaged = (byte[])file.Clone();
                damaged[offset] ^= 0x5A;
                Assert.Throws<InvalidDataException>(() => SnapshotFile.Decode(damaged), $"flipped byte at {offset}");
            }
        }

        public static void RejectsTruncatedAndPaddedFiles()
        {
            var file = SnapshotFile.Encode(Sections("a"));
            Assert.Throws<InvalidDataException>(() => SnapshotFile.Decode(file.Take(file.Length - 1).ToArray()), "truncated by one");
            Assert.Throws<InvalidDataException>(() => SnapshotFile.Decode(file.Take(10).ToArray()), "header only");
            Assert.Throws<InvalidDataException>(() => SnapshotFile.Decode(Array.Empty<byte>()), "empty");
            Assert.Throws<InvalidDataException>(() => SnapshotFile.Decode(file.Concat(new byte[] { 0 }).ToArray()), "padded");
        }

        public static void WriteKeepsPreviousSave()
        {
            using var dir = new TempDir();
            var path = dir.File("raid.spsave");
            SnapshotFile.Write(path, Sections("first"));
            SnapshotFile.Write(path, Sections("second"));
            Assert.True(Tag(SnapshotFile.Read(path)).Contains("second"), "current save is the second write");
            Assert.True(Tag(SnapshotFile.Read(path + SnapshotFile.PrevSuffix)).Contains("first"), ".prev holds the first write");
            Assert.True(!File.Exists(path + SnapshotFile.TempSuffix), "no .tmp left behind");
        }

        public static void TornTempFileFallsBackToCurrentSave()
        {
            // A crash while writing .tmp: the current save is untouched and still wins.
            using var dir = new TempDir();
            var path = dir.File("raid.spsave");
            SnapshotFile.Write(path, Sections("good"));
            var next = SnapshotFile.Encode(Sections("next"));
            File.WriteAllBytes(path + SnapshotFile.TempSuffix, next.Take(next.Length / 3).ToArray());

            var problems = new List<string>();
            var loaded = SnapshotFile.LoadNewestValid(path, out var from, problems);
            Assert.True(loaded != null && Tag(loaded).Contains("good"), "loads the last good save");
            Assert.Equal(path, from, "loaded from");
        }

        public static void CrashBetweenMovesLoadsTheVerifiedTemp()
        {
            // A crash after the current save moved to .prev but before .tmp moved into place.
            using var dir = new TempDir();
            var path = dir.File("raid.spsave");
            SnapshotFile.Write(path, Sections("old"));
            File.Move(path, path + SnapshotFile.PrevSuffix);
            File.WriteAllBytes(path + SnapshotFile.TempSuffix, SnapshotFile.Encode(Sections("new")));

            var loaded = SnapshotFile.LoadNewestValid(path, out var from, new List<string>());
            Assert.True(loaded != null && Tag(loaded).Contains("new"), "loads the newer, complete .tmp");
            Assert.Equal(path + SnapshotFile.TempSuffix, from, "loaded from");
        }

        public static void DamagedCurrentSaveFallsBackToPrevious()
        {
            using var dir = new TempDir();
            var path = dir.File("raid.spsave");
            SnapshotFile.Write(path, Sections("first"));
            SnapshotFile.Write(path, Sections("second"));
            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            var problems = new List<string>();
            var loaded = SnapshotFile.LoadNewestValid(path, out var from, problems);
            Assert.True(loaded != null && Tag(loaded).Contains("first"), "falls back to .prev");
            Assert.Equal(1, problems.Count, "one problem reported");
        }

        public static void NothingValidReturnsNull()
        {
            using var dir = new TempDir();
            var path = dir.File("raid.spsave");
            File.WriteAllText(path, "not a save");
            var problems = new List<string>();
            Assert.True(SnapshotFile.LoadNewestValid(path, out _, problems) == null, "no save loaded");
            Assert.Equal(1, problems.Count, "the bad file is reported");
        }

        public static void FailedWriteLeavesCurrentSaveAlone()
        {
            using var dir = new TempDir();
            var path = dir.File("raid.spsave");
            SnapshotFile.Write(path, Sections("good"));
            // A directory where the temp file should go makes the write fail.
            Directory.CreateDirectory(path + SnapshotFile.TempSuffix);
            Assert.Throws<UnauthorizedAccessException>(() => SnapshotFile.Write(path, Sections("next")), "write into a blocked temp path");
            Assert.True(Tag(SnapshotFile.Read(path)).Contains("good"), "current save unchanged");
            Assert.True(!File.Exists(path + SnapshotFile.PrevSuffix), "nothing rotated");
        }
    }
}
