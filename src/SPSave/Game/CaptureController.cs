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

        internal static void Attach(GameWorld world)
        {
            if (world.GetComponent<CaptureController>() == null)
            {
                Current = world.gameObject.AddComponent<CaptureController>();
                PitFireTeamCompat.Prepare();
            }
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
                    return;
                }
                if (report.InCombat)
                {
                    MarkSavedInCombat(result);
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
                var check = RoundTrip.Verify(SnapshotFile.Read(path), result.WorldItemIds, result.WorldItems, report.Corpses);
                report.RoundTripMs = watch.Elapsed.TotalMilliseconds;
                report.RoundTripOk = check.Ok;
                report.RoundTripDetail = check.Detail;
                report.FrameMs = frame.Elapsed.TotalMilliseconds;
                Plugin.Log.LogInfo("Captured " + report.Summary());

                if (!report.RoundTripOk)
                {
                    // A save that doesn't read back must never be resumed.
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning("Round trip failed, save discarded: " + report.RoundTripDetail);
                    NotificationManager.DisplayWarningNotification("SPSave: the save didn't check out, so it was discarded. Your raid continues.");
                    return;
                }

                // The lock goes on before the game closes: without it the stash and traders could change while
                // the save waits, and resuming would duplicate or lose gear. No lock, no save.
                var manifest = ReadManifest(result);
                if (manifest == null || !LockClient.Lock(manifest))
                {
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning("The server didn't confirm the lock; save discarded. Is the SPSave server mod installed?");
                    NotificationManager.DisplayWarningNotification("SPSave: the server didn't confirm the save (is the SPSave server mod installed?). Your raid continues.");
                    return;
                }
                Plugin.Log.LogInfo($"Profile locked on the server until this save is resumed or abandoned ({manifest.Map}, {manifest.ServerId}).");

                Quitting = true;
                _busy = true;
                NotificationManager.DisplayMessageNotification("SPSave: raid saved. Closing the game...");
                StartCoroutine(QuitSoon());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Save failed: " + e);
                NotificationManager.DisplayWarningNotification("SPSave: the save failed. Your raid continues. See the BepInEx log.");
            }
        }

        /// <summary>The manifest's ResolveAs makes the next load end the raid as Killed (ResolveRule).</summary>
        private static void MarkSavedInCombat(CaptureResult result)
        {
            for (var i = 0; i < result.Sections.Count; i++)
            {
                if (result.Sections[i].Key != RaidCapture.ManifestSection)
                {
                    continue;
                }
                var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(System.Text.Encoding.UTF8.GetString(result.Sections[i].Value));
                manifest.ResolveAs = ResolveRule.SavedInCombat;
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
