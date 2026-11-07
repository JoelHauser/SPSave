using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SPSave.Server
{
    /// <summary>A profile with a saved raid waiting. While it exists the profile can't change (Patches.cs).</summary>
    public sealed class RaidLock
    {
        [JsonPropertyName("profileId")] public string ProfileId { get; set; }
        [JsonPropertyName("map")] public string Map { get; set; }
        [JsonPropertyName("side")] public string Side { get; set; }
        /// <summary>The saved raid's serverId; the client checks its save against it.</summary>
        [JsonPropertyName("serverId")] public string ServerId { get; set; }
        [JsonPropertyName("lockedUtc")] public string LockedUtc { get; set; }
    }

    /// <summary>
    /// Locks, one file per profile in the mod's own folder (locks\&lt;profile id&gt;.json), never in user\profiles (the
    /// server loads profiles from there). Static because Harmony patches are static; set up once in SPSaveLoader.
    /// Written atomically (temp file, then replace) so a crash leaves the old lock or the new one.
    /// </summary>
    public static class LockStore
    {
        private static readonly ConcurrentDictionary<string, RaidLock> Locks = new ConcurrentDictionary<string, RaidLock>(StringComparer.Ordinal);
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };
        private static string _folder;

        public static void Load(string modFolder, Action<string> log)
        {
            _folder = Path.Combine(modFolder, "locks");
            Directory.CreateDirectory(_folder);
            foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
            {
                try
                {
                    var raidLock = JsonSerializer.Deserialize<RaidLock>(File.ReadAllText(file));
                    if (raidLock?.ProfileId != null)
                    {
                        Locks[raidLock.ProfileId] = raidLock;
                        log($"Profile {raidLock.ProfileId} is locked: a saved raid on {raidLock.Map} is waiting.");
                    }
                }
                catch (Exception e)
                {
                    log($"Unreadable lock {file} ignored: {e.Message}");
                }
            }
        }

        public static RaidLock Get(string profileId) =>
            profileId != null && Locks.TryGetValue(profileId, out var raidLock) ? raidLock : null;

        public static bool IsLocked(string profileId) => Get(profileId) != null;

        public static void Set(RaidLock raidLock)
        {
            var path = PathFor(raidLock.ProfileId);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(raidLock, Options));
            File.Move(temp, path, overwrite: true);
            Locks[raidLock.ProfileId] = raidLock;
        }

        /// <returns>True if there was a lock.</returns>
        public static bool Clear(string profileId)
        {
            if (profileId == null)
            {
                return false;
            }
            var path = PathFor(profileId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return Locks.TryRemove(profileId, out _);
        }

        private static string PathFor(string profileId)
        {
            foreach (var c in profileId)
            {
                if (!Uri.IsHexDigit(c))
                {
                    throw new ArgumentException($"Not a profile id: {profileId}");
                }
            }
            return Path.Combine(_folder, profileId + ".json");
        }
    }
}
