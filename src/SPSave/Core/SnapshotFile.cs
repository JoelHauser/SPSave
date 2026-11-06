using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SPSave.Core
{
    /// <summary>
    /// One save on disk: named byte sections, gzipped, guarded by a SHA-256 of the compressed body.
    /// Layout: "SPSAVE" | format byte | sha256 (32 bytes) | body length (int64) | body.
    /// Body (after gunzip): section count (int32), then per section: name (int32 length + UTF-8), data (int32 length + bytes).
    /// </summary>
    public static class SnapshotFile
    {
        public const byte FormatVersion = 1;
        public const string TempSuffix = ".tmp";
        public const string PrevSuffix = ".prev";

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SPSAVE");
        private const int HashLength = 32;
        private static readonly int HeaderLength = Magic.Length + 1 + HashLength + 8;

        public static byte[] Encode(IReadOnlyList<KeyValuePair<string, byte[]>> sections)
        {
            byte[] body;
            using (var compressed = new MemoryStream())
            {
                using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                using (var writer = new BinaryWriter(gzip, Encoding.UTF8))
                {
                    writer.Write(sections.Count);
                    foreach (var section in sections)
                    {
                        var name = Encoding.UTF8.GetBytes(section.Key);
                        writer.Write(name.Length);
                        writer.Write(name);
                        var data = section.Value ?? Array.Empty<byte>();
                        writer.Write(data.Length);
                        writer.Write(data);
                    }
                }
                body = compressed.ToArray();
            }

            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(body);
            }

            var file = new byte[HeaderLength + body.Length];
            Buffer.BlockCopy(Magic, 0, file, 0, Magic.Length);
            file[Magic.Length] = FormatVersion;
            Buffer.BlockCopy(hash, 0, file, Magic.Length + 1, HashLength);
            Buffer.BlockCopy(BitConverter.GetBytes((long)body.Length), 0, file, Magic.Length + 1 + HashLength, 8);
            Buffer.BlockCopy(body, 0, file, HeaderLength, body.Length);
            return file;
        }

        /// <summary>Parses and validates a whole file. Throws <see cref="InvalidDataException"/> on any damage.</summary>
        public static Dictionary<string, byte[]> Decode(byte[] file)
        {
            if (file == null || file.Length < HeaderLength)
            {
                throw new InvalidDataException("Save file is shorter than its header.");
            }
            for (var i = 0; i < Magic.Length; i++)
            {
                if (file[i] != Magic[i])
                {
                    throw new InvalidDataException("Not an SPSave file.");
                }
            }
            if (file[Magic.Length] != FormatVersion)
            {
                throw new InvalidDataException($"Save format {file[Magic.Length]} is not supported (expected {FormatVersion}).");
            }
            var bodyLength = BitConverter.ToInt64(file, Magic.Length + 1 + HashLength);
            if (bodyLength != file.Length - HeaderLength)
            {
                throw new InvalidDataException($"Save body is {file.Length - HeaderLength} bytes, header says {bodyLength}.");
            }

            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(file, HeaderLength, (int)bodyLength);
            }
            for (var i = 0; i < HashLength; i++)
            {
                if (hash[i] != file[Magic.Length + 1 + i])
                {
                    throw new InvalidDataException("Save checksum does not match its contents.");
                }
            }

            var sections = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                using (var gzip = new GZipStream(new MemoryStream(file, HeaderLength, (int)bodyLength), CompressionMode.Decompress))
                using (var reader = new BinaryReader(gzip, Encoding.UTF8))
                {
                    var count = reader.ReadInt32();
                    if (count < 0)
                    {
                        throw new InvalidDataException("Negative section count.");
                    }
                    for (var i = 0; i < count; i++)
                    {
                        var name = Encoding.UTF8.GetString(ReadExactly(reader, reader.ReadInt32()));
                        var data = ReadExactly(reader, reader.ReadInt32());
                        if (sections.ContainsKey(name))
                        {
                            throw new InvalidDataException($"Section '{name}' appears twice.");
                        }
                        sections.Add(name, data);
                    }
                }
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Save body ends early.", e);
            }
            return sections;
        }

        /// <summary>
        /// Writes to path + ".tmp", flushes to disk, reads it back and validates it, then moves the current save
        /// to ".prev" and the new one into place. A failure at any point leaves the last valid save loadable
        /// through <see cref="LoadNewestValid"/>.
        /// </summary>
        public static WriteResult Write(string path, IReadOnlyList<KeyValuePair<string, byte[]>> sections)
        {
            var result = new WriteResult();
            var watch = Stopwatch.StartNew();
            var bytes = Encode(sections);
            result.EncodeMs = Lap(watch);
            foreach (var section in sections)
            {
                result.RawBytes += section.Value?.Length ?? 0;
            }
            result.FileBytes = bytes.Length;

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + TempSuffix;
            var previous = path + PrevSuffix;
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                result.WriteMs = Lap(watch);

                Decode(File.ReadAllBytes(temp));
                result.VerifyMs = Lap(watch);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }

            if (File.Exists(path))
            {
                TryDelete(previous);
                File.Move(path, previous);
            }
            File.Move(temp, path);
            result.ReplaceMs = Lap(watch);
            return result;
        }

        public static Dictionary<string, byte[]> Read(string path)
        {
            return Decode(File.ReadAllBytes(path));
        }

        /// <summary>
        /// Loads the newest save that validates: the save itself, then a ".tmp" left by a write that stopped between
        /// its two moves, then ".prev". Returns null when none validates; <paramref name="problems"/> says why.
        /// </summary>
        public static Dictionary<string, byte[]> LoadNewestValid(string path, out string loadedFrom, List<string> problems)
        {
            foreach (var candidate in new[] { path, path + TempSuffix, path + PrevSuffix })
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }
                try
                {
                    var sections = Read(candidate);
                    loadedFrom = candidate;
                    return sections;
                }
                catch (Exception e) when (e is InvalidDataException || e is IOException)
                {
                    problems?.Add($"{Path.GetFileName(candidate)}: {e.Message}");
                }
            }
            loadedFrom = null;
            return null;
        }

        private static byte[] ReadExactly(BinaryReader reader, int length)
        {
            if (length < 0)
            {
                throw new InvalidDataException("Negative length in save body.");
            }
            var data = reader.ReadBytes(length);
            if (data.Length != length)
            {
                throw new EndOfStreamException();
            }
            return data;
        }

        private static double Lap(Stopwatch watch)
        {
            var ms = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            return ms;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public sealed class WriteResult
    {
        public long RawBytes;
        public long FileBytes;
        public double EncodeMs;
        public double WriteMs;
        public double VerifyMs;
        public double ReplaceMs;
    }
}
