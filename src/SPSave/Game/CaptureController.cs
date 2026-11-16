using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.UI;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Save &amp; Quit: capture the raid, write it, read it back, lock the profile on the server, and close the game.
    /// Quitting means GameEnd never runs, so the server gets no raid end: no extract, death or MIA.
    /// From the pause menu's SAVE &amp; QUIT (PauseMenuButton): in combat it asks first in the stock dialog, and a
    /// confirmed combat save is marked to end as Killed (ResolveRule). No hotkey and no per-frame code: the Ctrl+F10
    /// fallback went once the button worked in game (0.8.5).
    ///
    /// Autosave (1.0.3, opt-in, "Autosave every (minutes)"): the same capture, write, read-back and lock on a timer,
    /// without quitting, so a crash can be resumed. Skipped in combat (the last autosave stays), in Scav raids and
    /// while a resume finishes or a saved raid is being ended. A normal raid end discards it (RaidEndPatch); quitting
    /// the game normally (Alt-F4, closing the window) discards it and unlocks (OnQuitting), so only a crash or a
    /// killed process leaves it. Known hole, accepted: killing the game in Task Manager looks like a crash.
    /// </summary>
    internal sealed class CaptureController : MonoBehaviour
    {
        /// <summary>Set once a save is confirmed and the game is closing; RaidEndPatch must not discard that save.</summary>
        internal static bool Quitting;

        /// <summary>The raid's controller, for the pause-menu button.</summary>
        internal static CaptureController Current;

        /// <summary>
        /// Only PMC raids are saved: a resume rebuilds a PMC raid (ResumeSession.Begin, the server lock's side), so a
        /// Scav raid saved here could never come back, and its lock would hold the PMC profile.
        /// </summary>
        internal static bool ScavRaid => Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer?.Side == EPlayerSide.Savage;

        private const string CombatText =
            "You are in combat. If you save now, this raid will end as Killed when you come back, and you will lose the " +
            "gear you took in. Save and quit anyway?";

        private bool _busy;

        /// <summary>This raid has an autosave on disk (and a lock): a normal quit must discard both.</summary>
        private string _autosavePath;

        /// <summary>Real seconds between checks while autosave is off, so turning it on in F12 takes effect.</summary>
        private const float IdleCheckSeconds = 30f;

        internal static void Attach(GameWorld world)
        {
            if (world.GetComponent<CaptureController>() == null)
            {
                Current = world.gameObject.AddComponent<CaptureController>();
                PitFireTeamCompat.Prepare();
            }
        }

        private void Start()
        {
            Application.quitting += OnQuitting;
            StartCoroutine(AutoSaveLoop());
        }

        /// <summary>
        /// One wait per autosave, not per-frame code: the coroutine sleeps for the whole interval. With autosave off it
        /// only wakes every 30 s to see whether it was turned on.
        /// </summary>
        private IEnumerator AutoSaveLoop()
        {
            while (true)
            {
                var minutes = Plugin.AutoSaveMinutes.Value;
                if (minutes <= 0)
                {
                    yield return new WaitForSecondsRealtime(IdleCheckSeconds);
                    continue;
                }
                yield return new WaitForSecondsRealtime(minutes * 60f);
                if (Plugin.AutoSaveMinutes.Value > 0)
                {
                    StartAutoSave();
                }
            }
        }

        private void StartAutoSave()
        {
            if (_busy || _autosaving || Quitting || ScavRaid)
            {
                return;
            }
            var phase = ResumeSession.Phase;
            if (phase != ResumePhase.None && phase != ResumePhase.Live)
            {
                Plugin.Log.LogInfo($"Autosave skipped: a saved raid is still loading or ending ({phase}).");
                return;
            }
            var player = Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            {
                return;
            }
            StartCoroutine(AutoSaveRun());
        }

        // ---- Autosave without the freeze (1.1.0) ----
        //
        // Save & Quit does everything in one frame; the game closes after it, so the freeze doesn't matter. An autosave
        // keeps playing, and on Woods that frame was 834-981 ms, about half of it the read-back check. Only reading the
        // raid has to be one frame (one consistent moment: no item on a bot and on the ground at once). So:
        //   1. this frame: RaidCapture.Snapshot reads the raid into data (inventories and loot flattened to FlatItem[]);
        //   2. a worker thread: JSON, compress, write, read back, decode, parse (no game objects, only that data);
        //   3. later frames, a few ms each: the read-back's world loot and player are rebuilt into items (the game's
        //      ItemFactory isn't thread-safe) and compared with the capture; then the swap and the lock.
        // The worker writes "raid.spsave.pending", which a resume never reads; only a checked autosave replaces
        // raid.spsave (SnapshotFile.Promote), so a crash at any point leaves the last checked autosave or none. A
        // failed check throws away only the pending file: the last autosave and its lock stay.
        // A raid end, a normal quit or Save & Quit while it runs cancels it (CancelAutosave): it waits for the worker,
        // so no two writers meet, deletes the pending file, and the lock is never taken after that (the lock and the
        // cancel are both on the main thread).

        /// <summary>Main-thread milliseconds per frame for rebuilding the read-back's world loot.</summary>
        private const double SliceBudgetMs = 4.0;

        private bool _autosaving;

        /// <summary>The autosave between its capture frame and its lock, if one is running.</summary>
        private static AutosaveJob _job;

        private sealed class AutosaveJob
        {
            public CaptureSnapshot Snapshot;
            /// <summary>The save (raid.spsave), replaced only by a checked autosave.</summary>
            public string Path;
            /// <summary>Where the worker writes and the check reads (raid.spsave.pending).</summary>
            public string Pending;
            public volatile bool Cancelled;
            public Task Work;
            public WriteResult Write;
            public RoundTrip.Parsed Parsed;
            public double WorkerMs;
            public double ParseMs;
        }

        /// <summary>
        /// Stops a running autosave before anything else touches the save or the lock: marks it cancelled, waits for
        /// its worker (at most a few hundred ms) and deletes its pending file. The save and the lock are the caller's.
        /// Main thread only.
        /// </summary>
        internal static void CancelAutosave(string why)
        {
            var job = _job;
            if (job == null)
            {
                return;
            }
            _job = null;
            job.Cancelled = true;
            try
            {
                job.Work?.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception)
            {
                // A failed worker counts as cancelled.
            }
            SnapshotFile.DiscardPending(job.Pending);
            Plugin.Log.LogInfo($"Autosave in progress stopped ({why}).");
        }

        private IEnumerator AutoSaveRun()
        {
            _autosaving = true;
            AutosaveJob job = null;
            try
            {
                var total = Stopwatch.StartNew();
                job = BeginAutosave(out var frameMs);
                if (job == null)
                {
                    yield break;
                }
                _job = job;
                var frames = 0;
                while (!job.Work.IsCompleted)
                {
                    yield return null;
                    frames++;
                    if (job.Cancelled)
                    {
                        yield break;
                    }
                }
                if (job.Work.IsFaulted)
                {
                    DropPending(job, "The autosave failed: " + job.Work.Exception?.GetBaseException(), "SPSave: the autosave failed. See the BepInEx log.");
                    yield break;
                }

                // Rebuild the read-back's world loot a few entries per frame, then the player, on the main thread.
                var report = job.Snapshot.Result.Report;
                var problems = job.Parsed.Problems;
                var mainMs = 0d;
                var longestMs = 0d;
                var slice = new Stopwatch();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var corpses = 0;
                var loot = job.Parsed.Loot;
                if (loot != null)
                {
                    var serializer = RoundTrip.LootSerializer();
                    var next = 0;
                    while (next < loot.Count)
                    {
                        slice.Restart();
                        try
                        {
                            while (next < loot.Count && slice.Elapsed.TotalMilliseconds < SliceBudgetMs)
                            {
                                RoundTrip.ReadLootEntry(loot[next++], serializer, ids, ref corpses);
                            }
                        }
                        catch (Exception e)
                        {
                            problems.Add($"world loot does not decode: {e.Message}");
                            next = loot.Count;
                        }
                        mainMs += slice.Elapsed.TotalMilliseconds;
                        longestMs = Math.Max(longestMs, slice.Elapsed.TotalMilliseconds);
                        yield return null;
                        frames++;
                        if (job.Cancelled)
                        {
                            yield break;
                        }
                    }
                    slice.Restart();
                    RoundTrip.CompareWorld(ids, corpses, job.Snapshot.Result.WorldItemIds, job.Snapshot.Result.WorldItems, report.Corpses, problems);
                    report.Step("rt world loot (main, sliced)", mainMs + slice.Elapsed.TotalMilliseconds);
                }

                slice.Restart();
                try
                {
                    if (job.Parsed.Player != null)
                    {
                        RoundTrip.RebuildPlayer(job.Parsed.Player, job.Parsed.State, problems);
                    }
                    RoundTrip.CheckWaves(job.Parsed.State);
                }
                catch (Exception e)
                {
                    problems.Add($"the profile does not rebuild: {e.Message}");
                }
                var playerMs = slice.Elapsed.TotalMilliseconds;
                report.Step("rt player rebuild (main)", playerMs);
                mainMs += playerMs;
                longestMs = Math.Max(longestMs, playerMs);
                yield return null;
                frames++;
                if (job.Cancelled)
                {
                    yield break;
                }

                report.RoundTripOk = problems.Count == 0;
                report.RoundTripDetail = problems.Count == 0
                    ? $"{job.Parsed.Profiles} profiles and {ids.Count} world items round-trip"
                    : string.Join("; ", problems);
                report.RoundTripMs = job.ParseMs + mainMs;
                report.FrameMs = frameMs;
                Plugin.Log.LogInfo("Autosaved " + report.Summary());
                Plugin.Log.LogInfo("Save steps (ms): " + report.StepsLine());
                if (!report.RoundTripOk)
                {
                    DropPending(job, "Round trip failed, autosave discarded: " + report.RoundTripDetail, "SPSave: the autosave didn't check out, so it was discarded.");
                    yield break;
                }

                // Swap and lock, as for Save & Quit. Never after a cancel: the raid has ended or the game is closing.
                if (Quitting || job.Cancelled)
                {
                    yield break;
                }
                try
                {
                    SnapshotFile.Promote(job.Pending, job.Path);
                }
                catch (Exception e)
                {
                    DropPending(job, "The checked autosave couldn't replace the last one: " + e, "SPSave: the autosave failed. See the BepInEx log.");
                    yield break;
                }
                slice.Restart();
                var locked = LockClient.Lock(job.Snapshot.Manifest);
                var lockMs = slice.Elapsed.TotalMilliseconds;
                Plugin.Log.LogInfo($"Autosave timing: capture frame {frameMs:F0} ms; worker {job.WorkerMs:F0} ms (json, write, read back, parse); " +
                                   $"check on the main thread {mainMs:F0} ms over {frames} frames, longest {longestMs:F1} ms; lock {lockMs:F0} ms; " +
                                   $"done {total.Elapsed.TotalMilliseconds:F0} ms after it started.");
                if (!locked)
                {
                    DropUnlocked(job);
                    yield break;
                }
                _autosavePath = job.Path;
                Plugin.Log.LogInfo($"Profile locked on the server until this autosave is resumed or discarded ({job.Snapshot.Manifest.Map}, {job.Snapshot.Manifest.ServerId}).");
            }
            finally
            {
                if (_job == job)
                {
                    _job = null;
                }
                _autosaving = false;
            }
        }

        /// <summary>
        /// The capture frame: snapshot the raid and hand it to a worker. Null when in combat or the capture failed
        /// (the raid carries on either way).
        /// </summary>
        private AutosaveJob BeginAutosave(out double frameMs)
        {
            var frame = Stopwatch.StartNew();
            frameMs = 0;
            try
            {
                var snapshot = RaidCapture.Snapshot(Plugin.CombatWindowSeconds.Value);
                frameMs = frame.Elapsed.TotalMilliseconds;
                if (snapshot.Result.Report.InCombat)
                {
                    Plugin.Log.LogInfo((_autosavePath != null
                        ? "Autosave skipped: in combat. The last autosave stays."
                        : "Autosave skipped: in combat.") + $" ({frameMs:F0} ms)");
                    return null;
                }
                snapshot.Manifest.AutoSave = true;
                var path = Plugin.SavePath(snapshot.Result.ProfileId);
                var job = new AutosaveJob { Snapshot = snapshot, Path = path, Pending = path + SnapshotFile.PendingSuffix };
                job.Work = Task.Run(() => Work(job));
                return job;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("The autosave failed: " + e);
                NotificationManager.DisplayWarningNotification("SPSave: the autosave failed. See the BepInEx log.");
                return null;
            }
        }

        /// <summary>
        /// Worker thread: only the snapshot's data, files and plain parsing; no game objects, no Unity calls, no
        /// logging (the main thread logs the outcome).
        /// </summary>
        private static void Work(AutosaveJob job)
        {
            var watch = Stopwatch.StartNew();
            RaidCapture.Serialize(job.Snapshot);
            if (job.Cancelled)
            {
                return;
            }
            var report = job.Snapshot.Result.Report;
            SnapshotFile.DiscardPending(job.Pending);
            job.Write = SnapshotFile.Write(job.Pending, job.Snapshot.Result.Sections);
            report.RawBytes = job.Write.RawBytes;
            report.FileBytes = job.Write.FileBytes;
            report.EncodeMs = job.Write.EncodeMs;
            report.WriteMs = job.Write.WriteMs + job.Write.ReplaceMs;
            report.VerifyFileMs = job.Write.VerifyMs;
            var parse = Stopwatch.StartNew();
            var readBack = SnapshotFile.Read(job.Pending);
            report.Step("rt file read", parse.Elapsed.TotalMilliseconds);
            parse.Restart();
            job.Parsed = RoundTrip.ParseOffMainThread(readBack);
            job.ParseMs = parse.Elapsed.TotalMilliseconds;
            report.Step("rt parse (worker)", job.ParseMs);
            job.WorkerMs = watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// An autosave that failed or didn't check out never reached raid.spsave: only its pending file goes. The last
        /// autosave, if any, stays with its lock.
        /// </summary>
        private void DropPending(AutosaveJob job, string detail, string notice)
        {
            Plugin.Log.LogWarning(detail + (_autosavePath != null ? " The last autosave stays." : ""));
            NotificationManager.DisplayWarningNotification(notice);
            SnapshotFile.DiscardPending(job.Pending);
        }

        /// <summary>
        /// The server didn't confirm the lock (no lock, no save). The checked autosave already replaced the last one,
        /// so the save goes (Retire takes ".prev" too) and any earlier lock with it.
        /// </summary>
        private void DropUnlocked(AutosaveJob job)
        {
            Plugin.Log.LogWarning("The server didn't confirm the lock; autosave discarded. Is the SPSave server mod installed?");
            NotificationManager.DisplayWarningNotification("SPSave: the server didn't confirm the autosave (is the SPSave server mod installed?).");
            try
            {
                SnapshotFile.Retire(job.Path, DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Discarding the unlocked autosave failed: " + e);
            }
            if (_autosavePath != null)
            {
                _autosavePath = null;
                LockClient.Unlock();
            }
        }

        /// <summary>
        /// A normal quit (Alt-F4, closing the window, Application.Quit) is not a crash: the autosave goes and the lock
        /// with it, as standard SPT scraps a raid the game was closed out of. A crash never gets here.
        /// </summary>
        private void OnQuitting()
        {
            CancelAutosave("the game is closing");
            if (Quitting || _autosavePath == null)
            {
                return;
            }
            try
            {
                var retired = SnapshotFile.Retire(_autosavePath, DateTime.UtcNow);
                LockClient.Unlock();
                Plugin.Log.LogInfo($"Game closed normally during the raid: autosave discarded ({retired}) and profile unlocked.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Discarding the autosave on quit failed: " + e);
            }
            _autosavePath = null;
        }

        /// <summary>The pause menu's SAVE &amp; QUIT. A running autosave stops first; Save &amp; Quit overwrites its file.</summary>
        internal void RequestFromMenu()
        {
            if (_busy)
            {
                return;
            }
            SaveAndQuit(combatConfirmed: false);
        }

        private void OnDestroy()
        {
            Application.quitting -= OnQuitting;
            // The raid is over (RaidEndPatch already stopped a running autosave); a coroutine stops with this object.
            CancelAutosave("the raid is over");
            if (Current == this)
            {
                Current = null;
            }
            // The raid is over: whatever a resume left behind no longer applies.
            ResumeSession.Reset();
        }

        private void SaveAndQuit(bool combatConfirmed)
        {
            if (ScavRaid)
            {
                NotificationManager.DisplayWarningNotification("SPSave: only PMC raids can be saved.");
                return;
            }
            if (ResumeSession.Phase == ResumePhase.Finishing)
            {
                NotificationManager.DisplayWarningNotification("SPSave: wait until the raid has loaded, then save.");
                return;
            }

            // Also after the combat dialog: an autosave may have started while it was open. Save & Quit overwrites it.
            CancelAutosave("Save & Quit");
            var path = CaptureWriteLock(combatConfirmed);
            if (path == null)
            {
                return;
            }
            Quitting = true;
            _busy = true;
            NotificationManager.DisplayMessageNotification("SPSave: raid saved. Closing the game...");
            StartCoroutine(QuitSoon());
        }

        /// <summary>
        /// Captures the raid, writes it, reads it back and locks the profile. Returns the save's path, or null when
        /// nothing was kept (in combat without confirmation, a failed check, no lock, an error). Save &amp; Quit asks
        /// first in combat. Autosaves take their own path (AutoSaveRun).
        /// </summary>
        private string CaptureWriteLock(bool combatConfirmed)
        {
            const string what = "save";
            var frame = Stopwatch.StartNew();
            try
            {
                // Combat is judged at this moment (DESIGN.md), also after a confirmation: a player who broke contact
                // while answering saves normally.
                var result = RaidCapture.Run(Plugin.CombatWindowSeconds.Value);
                var report = result.Report;
               if (report.InCombat && !combatConfirmed)
                {
                    Plugin.Log.LogInfo("Save in combat, asking first: " + report.CombatDetail);
                    // forceShow: in a raid (InGameStatus.InRaid) ItemUiContext.method_1 skips the window and runs the
                    // accept action at once (0.8.0/0.8.1 saved in combat without asking).
                    ItemUiContext.Instance.ShowMessageWindow(CombatText,
                        () => SaveAndQuit(combatConfirmed: true),
                        () => Plugin.Log.LogInfo("Save in combat cancelled: the raid goes on."),
                        "CONFIRMATION".Localized(), forceShow: true);
                    return null;
                }
                if (report.InCombat)
                {
                    EditManifest(result, m => m.ResolveAs = ResolveRule.SavedInCombat);
                    Plugin.Log.LogInfo("Saving in combat (confirmed): this raid will end as Killed. " + report.CombatDetail);
                }

                var path = Plugin.SavePath(result.ProfileId);
                var write = SnapshotFile.Write(path, result.Sections);
                report.RawBytes = write.RawBytes;
                report.FileBytes = write.FileBytes;
                report.EncodeMs = write.EncodeMs;
                report.WriteMs = write.WriteMs + write.ReplaceMs;
                report.VerifyFileMs = write.VerifyMs;

                // Read the save back: one that doesn't decode into what was captured is never kept.
                var watch = Stopwatch.StartNew();
                var readBack = SnapshotFile.Read(path);
                report.Step("rt file read", watch.Elapsed.TotalMilliseconds);
                var check = RoundTrip.Verify(readBack, result.WorldItemIds, result.WorldItems, report.Corpses, report);
                report.RoundTripMs = watch.Elapsed.TotalMilliseconds;
                report.RoundTripOk = check.Ok;
                report.RoundTripDetail = check.Detail;
                report.FrameMs = frame.Elapsed.TotalMilliseconds;
                Plugin.Log.LogInfo("Captured " + report.Summary());
                Plugin.Log.LogInfo("Save steps (ms): " + report.StepsLine());

                if (!report.RoundTripOk)
                {
                    // A save that doesn't read back must never be resumed. An autosave overwrote the last one, so
                    // none is left (also after Save & Quit overwrote an autosave): its lock goes too.
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning($"Round trip failed, {what} discarded: " + report.RoundTripDetail);
                    NotificationManager.DisplayWarningNotification("SPSave: the save didn't check out, so it was discarded. Your raid continues.");
                    if (_autosavePath != null)
                    {
                        _autosavePath = null;
                        LockClient.Unlock();
                    }
                    return null;
                }

                // The lock goes on before the game closes: without it the stash and traders could change while
                // the save waits, and resuming would duplicate or lose gear. No lock, no save.
                var manifest = ReadManifest(result);
                var lockWatch = Stopwatch.StartNew();
                var locked = manifest != null && LockClient.Lock(manifest);
                Plugin.Log.LogInfo($"Server lock call: {lockWatch.Elapsed.TotalMilliseconds:F0} ms (main thread; {frame.Elapsed.TotalMilliseconds:F0} ms in all).");
                if (!locked)
                {
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning($"The server didn't confirm the lock; {what} discarded. Is the SPSave server mod installed?");
                    NotificationManager.DisplayWarningNotification("SPSave: the server didn't confirm the save (is the SPSave server mod installed?). Your raid continues.");
                    if (_autosavePath != null)
                    {
                        _autosavePath = null;
                        LockClient.Unlock();
                    }
                    return null;
                }
                Plugin.Log.LogInfo($"Profile locked on the server until this {what} is resumed or discarded ({manifest.Map}, {manifest.ServerId}).");
                return path;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"The {what} failed: " + e);
                NotificationManager.DisplayWarningNotification("SPSave: the save failed. Your raid continues. See the BepInEx log.");
                return null;
            }
        }

        /// <summary>
        /// Changes the captured manifest: ResolveAs makes the next load end the raid as Killed (ResolveRule); AutoSave
        /// marks a timer save.
        /// </summary>
        private static void EditManifest(CaptureResult result, Action<Manifest> edit)
        {
            for (var i = 0; i < result.Sections.Count; i++)
            {
                if (result.Sections[i].Key != RaidCapture.ManifestSection)
                {
                    continue;
                }
                var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(System.Text.Encoding.UTF8.GetString(result.Sections[i].Value));
                edit(manifest);
                var bytes = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(manifest, Newtonsoft.Json.Formatting.Indented));
                result.Sections[i] = new System.Collections.Generic.KeyValuePair<string, byte[]>(RaidCapture.ManifestSection, bytes);
                return;
            }
            throw new InvalidOperationException("The capture has no manifest.");
        }

        private static Manifest ReadManifest(CaptureResult result)
        {
            foreach (var section in result.Sections)
            {
                if (section.Key == RaidCapture.ManifestSection)
                {
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(System.Text.Encoding.UTF8.GetString(section.Value));
                }
            }
            return null;
        }

        private static IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(2f);
            Plugin.Log.LogInfo("Closing the game after Save & Quit.");
            Application.Quit();
        }
    }
}
