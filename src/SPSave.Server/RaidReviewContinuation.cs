using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace SPSave.Server
{
    /// <summary>
    /// Raid Review (ekky.raidreview, optional) keeps one raid per START packet: WsPacketHandler makes a new raid id,
    /// sets it on the profile (SessionManagerPlayer.RaidId, which every later packet is filed under), registers it
    /// (SessionManager.AddRaid) and inserts its row. END fills in the exit and queues the positions compile. A raid
    /// without END keeps exitStatus '' and its cleanup (GarbageCollector, at start and hourly) deletes it.
    ///
    /// So a saved raid's halves are joined here, as one raid:
    /// - At the lock (Save &amp; Quit) the profile's current Raid Review raid is noted in the lock and marked exit
    ///   'Left', which the cleanup keeps (its timeInRaid stays 0, so the keep-the-last-30 purge skips it too); its
    ///   positions are flushed and compiled, so the half can be viewed while the save waits.
    /// - At the resume's START (the lock is still there; it goes at Ready) AddRaid is given the saved raid's id
    ///   instead, so every packet of the second half is filed under it and its positions go on in the same file.
    ///   The new id's row insert becomes an INSERT OR IGNORE of the saved id (a no-op unless the row was lost). The
    ///   client sets Raid Review's clock on, so times follow on (SPSave.Game.RaidReviewCompat). END then finishes
    ///   the saved raid with the real exit and compiles both halves.
    /// - Restored bots are registered again: their PLAYER rows are ignored (unique raid + profile), and their
    ///   loadout rows (player_inventory, no unique key) are skipped when the first half has them.
    /// - At END the raid's created_at and time become the resume's: Raid Review's purge keeps the last 30 raids by
    ///   created_at, and a raid dated from before a long wait would be the first to go.
    /// - A save thrown away without a resume (LockStore.Clear with no resume START for its raid) gets its recording
    ///   finished: timeInRaid from the lock, so it reads as an ordinary ended raid ('Left') that the purge handles.
    /// Members checked against Raid Review's server 1.9.0.
    /// </summary>
    internal static class RaidReviewContinuation
    {
        private static object _handler;
        private static FieldInfo _sessionManagerField, _dbField, _fileServiceField, _postProcessField;
        private static MethodInfo _getProfile, _execute, _query, _flush;
        private static PropertyInfo _profileRaidId, _raidDataRaidId, _raidDataPlayers;

        /// <summary>The resume's own new raid id and the saved one it stands for, from AddRaid to the row insert.</summary>
        private static volatile string _strayId;
        private static volatile string _continuedId;
        private static Task<List<Dictionary<string, object>>> _firstHalfLoadouts;
        /// <summary>The resume START's own time (its raid row insert), for the joined raid's date.</summary>
        private static volatile string _resumeTime;

        /// <summary>Raids a resume has carried on this server run; never finished as thrown away.</summary>
        private static readonly ConcurrentDictionary<string, bool> Continued = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Thrown-away recordings to finish once Raid Review's services are known (first packet).</summary>
        private static readonly ConcurrentQueue<(string RaidId, long Ms)> PendingFinish = new ConcurrentQueue<(string, long)>();

        internal static string Enable()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RaidReview");
            if (assembly == null)
            {
                return null;
            }
            var handler = assembly.GetType("RaidReview.WebSocket.WsPacketHandler");
            var sessions = assembly.GetType("RaidReview.Session.SessionManager");
            var player = assembly.GetType("RaidReview.Session.SessionManagerPlayer");
            var raid = assembly.GetType("RaidReview.Session.SessionManagerRaid");
            var db = assembly.GetType("RaidReview.Database.DatabaseService");
            var files = assembly.GetType("RaidReview.FileSystem.DataFileService");
            var paramsType = typeof(ValueTuple<string, object>[]);

            var handle = handler != null ? AccessTools.Method(handler, "HandleMessageAsync", new[] { typeof(byte[]) }) : null;
            var addRaid = sessions != null && raid != null ? AccessTools.Method(sessions, "AddRaid", new[] { typeof(string), raid }) : null;
            _execute = db != null ? AccessTools.Method(db, "ExecuteAsync", new[] { typeof(string), paramsType }) : null;
            _query = db != null ? AccessTools.Method(db, "QueryAsync", new[] { typeof(string), paramsType }) : null;
            _getProfile = sessions != null ? AccessTools.Method(sessions, "GetProfile", new[] { typeof(string) }) : null;
            _flush = files != null ? AccessTools.Method(files, "FlushAndCloseAll", Type.EmptyTypes) : null;
            _profileRaidId = player != null ? AccessTools.Property(player, "RaidId") : null;
            _raidDataRaidId = raid != null ? AccessTools.Property(raid, "RaidId") : null;
            _raidDataPlayers = raid != null ? AccessTools.Property(raid, "Players") : null;
            _sessionManagerField = handler != null ? AccessTools.Field(handler, "_sessionManager") : null;
            _dbField = handler != null ? AccessTools.Field(handler, "_db") : null;
            _fileServiceField = handler != null ? AccessTools.Field(handler, "_fileService") : null;
            _postProcessField = handler != null ? AccessTools.Field(handler, "_startPostProcessing") : null;

            var missing = new Dictionary<string, object>
            {
                ["WsPacketHandler.HandleMessageAsync"] = handle, ["SessionManager.AddRaid"] = addRaid,
                ["DatabaseService.ExecuteAsync"] = _execute, ["DatabaseService.QueryAsync"] = _query,
                ["SessionManager.GetProfile"] = _getProfile, ["DataFileService.FlushAndCloseAll"] = _flush,
                ["SessionManagerPlayer.RaidId"] = _profileRaidId, ["SessionManagerRaid.RaidId"] = _raidDataRaidId,
                ["SessionManagerRaid.Players"] = _raidDataPlayers, ["WsPacketHandler._sessionManager"] = _sessionManagerField,
                ["WsPacketHandler._db"] = _dbField, ["WsPacketHandler._fileService"] = _fileServiceField,
                ["WsPacketHandler._startPostProcessing"] = _postProcessField
            }.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
            if (missing.Count > 0)
            {
                return "Raid Review found, but NOT joined across saves (not found: " + string.Join(", ", missing) + ")";
            }

            var harmony = new Harmony("com.mybutthasarash.spsave.raidreview");
            harmony.Patch(handle, prefix: new HarmonyMethod(typeof(RaidReviewContinuation), nameof(HandlePrefix)));
            harmony.Patch(addRaid, prefix: new HarmonyMethod(typeof(RaidReviewContinuation), nameof(AddRaidPrefix)));
            harmony.Patch(_execute, prefix: new HarmonyMethod(typeof(RaidReviewContinuation), nameof(ExecutePrefix)));
            return "Raid Review found: a saved raid's halves are recorded as one raid";
        }

        /// <summary>Raid Review builds its services in its own OnLoad; they're picked up from the first packet.</summary>
        private static void HandlePrefix(object __instance)
        {
            if (_handler == null)
            {
                _handler = __instance;
            }
            while (!PendingFinish.IsEmpty && PendingFinish.TryDequeue(out var pending))
            {
                Finish(pending.RaidId, pending.Ms);
            }
        }

        /// <summary>
        /// The lock is gone. If its raid was never carried on by a resume, the save was thrown away (another game
        /// build, a save and lock that don't match, a wiped or removed profile): finish the held recording.
        /// </summary>
        internal static void Release(RaidLock raidLock)
        {
            var raidId = raidLock?.RaidReviewRaidId;
            if (string.IsNullOrEmpty(raidId) || Continued.ContainsKey(raidId) || _execute == null)
            {
                return;
            }
            var ms = raidLock.RaidReviewElapsedMs ?? 0;
            if (ms <= 0)
            {
                SPSaveLoader.Log($"Raid Review: saved raid {raidId} was thrown away; its length wasn't saved, so it stays as it is.");
                return;
            }
            if (_handler == null)
            {
                PendingFinish.Enqueue((raidId, ms));
                return;
            }
            Finish(raidId, ms);
        }

        private static void Finish(string raidId, long ms)
        {
            try
            {
                var handler = _handler;
                var update = (Task)_execute.Invoke(_dbField.GetValue(handler), new object[]
                {
                    "UPDATE raid SET timeInRaid = $timeInRaid WHERE raidId = $raidId AND exitStatus = 'Left' AND timeInRaid = '0'",
                    new[] { ("$timeInRaid", (object)ms.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("$raidId", (object)raidId) }
                });
                update.ContinueWith(t => SPSaveLoader.Log("Raid Review: finishing the thrown-away raid failed: " + t.Exception?.GetBaseException().Message),
                    TaskContinuationOptions.OnlyOnFaulted);
                _flush.Invoke(_fileServiceField.GetValue(handler), null);
                (_postProcessField.GetValue(handler) as Action<string>)?.Invoke(raidId);
                SPSaveLoader.Log($"Raid Review: saved raid {raidId} was thrown away; recorded as left after {ms / 1000} s.");
            }
            catch (Exception e)
            {
                SPSaveLoader.Log("Raid Review: finishing the thrown-away raid failed: " + e.GetBaseException().Message);
            }
        }

        /// <summary>
        /// At the lock: the Raid Review raid the profile is recording, kept so the resume can carry on with it.
        /// Null when Raid Review isn't recording this profile (or saw no packet since the server started).
        /// </summary>
        internal static string Hold(string profileId)
        {
            var handler = _handler;
            if (handler == null || _getProfile == null)
            {
                return null;
            }
            try
            {
                var profile = _getProfile.Invoke(_sessionManagerField.GetValue(handler), new object[] { profileId });
                var raidId = profile != null ? _profileRaidId.GetValue(profile) as string : null;
                if (string.IsNullOrEmpty(raidId))
                {
                    return null;
                }
                var db = _dbField.GetValue(handler);
                var update = (Task)_execute.Invoke(db, new object[]
                {
                    "UPDATE raid SET exitStatus = 'Left' WHERE raidId = $raidId AND exitStatus = ''",
                    new[] { ("$raidId", (object)raidId) }
                });
                update.ContinueWith(t => SPSaveLoader.Log("Raid Review: marking the saved raid failed: " + t.Exception?.GetBaseException().Message),
                    TaskContinuationOptions.OnlyOnFaulted);
                _flush.Invoke(_fileServiceField.GetValue(handler), null);
                (_postProcessField.GetValue(handler) as Action<string>)?.Invoke(raidId);
                return raidId;
            }
            catch (Exception e)
            {
                SPSaveLoader.Log("Raid Review: the raid being recorded could not be kept for the resume: " + e.GetBaseException().Message);
                return null;
            }
        }

        /// <summary>The resume's START: register the saved raid instead of a new one.</summary>
        private static void AddRaidPrefix(object __instance, ref string raidId, object raidData)
        {
            try
            {
                var players = _raidDataPlayers.GetValue(raidData) as IDictionary;
                var profileId = players?.Keys.Cast<object>().FirstOrDefault() as string;
                var saved = LockStore.Get(profileId)?.RaidReviewRaidId;
                if (string.IsNullOrEmpty(saved) || saved == raidId)
                {
                    return;
                }
                _strayId = raidId;
                _continuedId = saved;
                _resumeTime = null;
                Continued[saved] = true;
                raidId = saved;
                _raidDataRaidId.SetValue(raidData, saved);
                var profile = _getProfile.Invoke(__instance, new object[] { profileId });
                if (profile != null)
                {
                    _profileRaidId.SetValue(profile, saved);
                }
                var handler = _handler;
                _firstHalfLoadouts = handler == null ? null : (Task<List<Dictionary<string, object>>>)_query.Invoke(_dbField.GetValue(handler), new object[]
                {
                    "SELECT DISTINCT profileId FROM player_inventory WHERE raidId = $raidId",
                    new[] { ("$raidId", (object)saved) }
                });
                SPSaveLoader.Log($"Raid Review: the resumed raid carries on recording {saved} (profile {profileId}).");
            }
            catch (Exception e)
            {
                SPSaveLoader.Log("Raid Review: the resumed raid could not carry on the saved recording; it is a new raid. " + e.GetBaseException().Message);
            }
        }

        private static bool ExecutePrefix(ref string sql, ref ValueTuple<string, object>[] parameters, ref Task __result)
        {
            var continued = _continuedId;
            if (continued == null || sql == null || parameters == null)
            {
                return true;
            }
            try
            {
                var stray = _strayId;
                if (stray != null && sql.StartsWith("INSERT INTO raid ", StringComparison.Ordinal))
                {
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].Item1 == "$raidId" && parameters[i].Item2 as string == stray)
                        {
                            parameters[i] = ("$raidId", continued);
                            sql = "INSERT OR IGNORE INTO raid " + sql.Substring("INSERT INTO raid ".Length);
                            _strayId = null;
                            _resumeTime = Value(parameters, "$time");
                            break;
                        }
                    }
                    return true;
                }
                if (sql.StartsWith("INSERT INTO player_inventory ", StringComparison.Ordinal) && Value(parameters, "$raidId") == continued)
                {
                    var loaded = _firstHalfLoadouts;
                    var profileId = Value(parameters, "$profileId");
                    if (loaded != null && loaded.IsCompletedSuccessfully && profileId != null &&
                        loaded.Result.Any(row => row.TryGetValue("profileId", out var id) && id as string == profileId))
                    {
                        __result = Task.CompletedTask;
                        return false;
                    }
                    return true;
                }
                if (sql.StartsWith("UPDATE raid SET timeInRaid", StringComparison.Ordinal) && Value(parameters, "$raidId") == continued)
                {
                    // END of the resumed raid: the saved raid is finished, dated from the resume so the
                    // keep-the-last-30 purge counts it as the newest raid.
                    const string head = "UPDATE raid SET ";
                    var time = _resumeTime;
                    if (sql.StartsWith(head, StringComparison.Ordinal))
                    {
                        sql = head + "created_at = CURRENT_TIMESTAMP, " + (time != null ? "time = $resumeTime, " : "") + sql.Substring(head.Length);
                        if (time != null)
                        {
                            Array.Resize(ref parameters, parameters.Length + 1);
                            parameters[parameters.Length - 1] = ("$resumeTime", time);
                        }
                    }
                    _resumeTime = null;
                    _continuedId = null;
                    _strayId = null;
                    _firstHalfLoadouts = null;
                    SPSaveLoader.Log($"Raid Review: saved raid {continued} finished.");
                }
            }
            catch (Exception)
            {
                // Never stand in Raid Review's way: run its statement as it is.
            }
            return true;
        }

        private static string Value(ValueTuple<string, object>[] parameters, string name)
        {
            foreach (var p in parameters)
            {
                if (p.Item1 == name)
                {
                    return p.Item2 as string;
                }
            }
            return null;
        }
    }
}
