using System;
using System.Collections;
using System.Diagnostics;
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
                    AutoSave();
                }
            }
        }

        private void AutoSave()
        {
            if (_busy || Quitting || ScavRaid)
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
            var path = CaptureWriteLock(autosave: true, combatConfirmed: false, out var inCombat);
            if (path != null)
            {
                _autosavePath = path;
            }
            else if (inCombat)
            {
                Plugin.Log.LogInfo(_autosavePath != null
                    ? "Autosave skipped: in combat. The last autosave stays."
                    : "Autosave skipped: in combat.");
            }
        }

        /// <summary>
        /// A normal quit (Alt-F4, closing the window, Application.Quit) is not a crash: the autosave goes and the lock
        /// with it, as standard SPT scraps a raid the game was closed out of. A crash never gets here.
        /// </summary>
        private void OnQuitting()
        {
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

        /// <summary>The pause menu's SAVE &amp; QUIT.</summary>
        internal void RequestFromMenu()
        {
            if (!_busy)
            {
                SaveAndQuit(combatConfirmed: false);
            }
        }

        private void OnDestroy()
        {
            Application.quitting -= OnQuitting;
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

            var path = CaptureWriteLock(autosave: false, combatConfirmed, out _);
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
        /// first in combat; an autosave skips the save instead.
        /// </summary>
        private string CaptureWriteLock(bool autosave, bool combatConfirmed, out bool inCombat)
        {
            inCombat = false;
            var what = autosave ? "autosave" : "save";
            var frame = Stopwatch.StartNew();
            try
            {
                // Combat is judged at this moment (DESIGN.md), also after a confirmation: a player who broke contact
                // while answering saves normally.
                var result = RaidCapture.Run(Plugin.CombatWindowSeconds.Value);
                var report = result.Report;
                inCombat = report.InCombat;
                if (report.InCombat && autosave)
                {
                    return null;
                }
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
                if (autosave)
                {
                    EditManifest(result, m => m.AutoSave = true);
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
                var check = RoundTrip.Verify(SnapshotFile.Read(path), result.WorldItemIds, result.WorldItems, report.Corpses);
                report.RoundTripMs = watch.Elapsed.TotalMilliseconds;
                report.RoundTripOk = check.Ok;
                report.RoundTripDetail = check.Detail;
                report.FrameMs = frame.Elapsed.TotalMilliseconds;
                Plugin.Log.LogInfo((autosave ? "Autosaved " : "Captured ") + report.Summary());

                if (!report.RoundTripOk)
                {
                    // A save that doesn't read back must never be resumed. An autosave overwrote the last one, so
                    // none is left (also after Save & Quit overwrote an autosave): its lock goes too.
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning($"Round trip failed, {what} discarded: " + report.RoundTripDetail);
                    NotificationManager.DisplayWarningNotification(autosave
                        ? "SPSave: the autosave didn't check out, so it was discarded."
                        : "SPSave: the save didn't check out, so it was discarded. Your raid continues.");
                    if (_autosavePath != null)
                    {
                        _autosavePath = null;
                        LockClient.Unlock();
                    }
                    return null;
                }

                // The lock goes on before the game closes: without it the stash and traders could change while
                // the save waits, and resuming would duplicate or lose gear. No lock, no save. An autosave locks
                // too: after a crash there is no later chance.
                var manifest = ReadManifest(result);
                if (manifest == null || !LockClient.Lock(manifest))
                {
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning($"The server didn't confirm the lock; {what} discarded. Is the SPSave server mod installed?");
                    NotificationManager.DisplayWarningNotification(autosave
                        ? "SPSave: the server didn't confirm the autosave (is the SPSave server mod installed?)."
                        : "SPSave: the server didn't confirm the save (is the SPSave server mod installed?). Your raid continues.");
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
                NotificationManager.DisplayWarningNotification(autosave
                    ? "SPSave: the autosave failed. See the BepInEx log."
                    : "SPSave: the save failed. Your raid continues. See the BepInEx log.");
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
