using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using EFT;
using EFT.Communications;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Save &amp; Quit on a hotkey (the pause-menu button comes later): capture the raid, write it, read it back, and
    /// close the game. Quitting means GameEnd never runs, so the server gets no raid end: no extract, death or MIA.
    /// Refused in combat for now; Joel's rule (a combat save resolves as Killed) needs the server mod (Step C).
    /// Its Update is the mod's only per-frame code during play: one key check.
    /// </summary>
    internal sealed class CaptureController : MonoBehaviour
    {
        /// <summary>Set once a save is confirmed and the game is closing; RaidEndPatch must not discard that save.</summary>
        internal static bool Quitting;

        private bool _busy;

        internal static void Attach(GameWorld world)
        {
            if (world.GetComponent<CaptureController>() == null)
            {
                world.gameObject.AddComponent<CaptureController>();
            }
        }

        private void OnDestroy()
        {
            // The raid is over: whatever a resume left behind no longer applies.
            ResumeSession.Reset();
        }

        private void Update()
        {
            if (!_busy && Plugin.CaptureKey.Value.IsDown())
            {
                SaveAndQuit();
            }
        }

        private void SaveAndQuit()
        {
            if (ResumeSession.Phase == ResumePhase.WaitingForReady)
            {
                NotificationManager.DisplayWarningNotification("SPSave: take control first (Ctrl+F11), then save.");
                return;
            }

            var frame = Stopwatch.StartNew();
            try
            {
                var result = RaidCapture.Run(Plugin.CombatWindowSeconds.Value);
                var report = result.Report;
                if (report.InCombat)
                {
                    Plugin.Log.LogInfo("Save refused: " + report.CombatDetail);
                    NotificationManager.DisplayWarningNotification("SPSave: you're in combat. Break contact for 30 seconds, then save.");
                    return;
                }

                var path = Plugin.SavePath(result.ProfileId);
                var write = SnapshotFile.Write(path, result.Sections);
                report.RawBytes = write.RawBytes;
                report.FileBytes = write.FileBytes;
                report.EncodeMs = write.EncodeMs;
                report.WriteMs = write.WriteMs + write.ReplaceMs;
                report.VerifyFileMs = write.VerifyMs;

                if (Plugin.VerifyRoundTrip.Value)
                {
                    var watch = Stopwatch.StartNew();
                    var check = RoundTrip.Verify(SnapshotFile.Read(path), result.WorldItemIds, result.WorldItems, report.Corpses);
                    report.RoundTripMs = watch.Elapsed.TotalMilliseconds;
                    report.RoundTripOk = check.Ok;
                    report.RoundTripDetail = check.Detail;
                }
                else
                {
                    report.RoundTripOk = true;
                    report.RoundTripDetail = "skipped";
                }
                report.FrameMs = frame.Elapsed.TotalMilliseconds;
                CaptureReport.AppendTo(Path.Combine(Plugin.SaveRoot, "captures.csv"), report);
                Plugin.Log.LogInfo("Captured " + report.Summary());

                if (!report.RoundTripOk)
                {
                    // A save that doesn't read back must never be resumed.
                    SnapshotFile.Retire(path, DateTime.UtcNow);
                    Plugin.Log.LogWarning("Round trip failed, save discarded: " + report.RoundTripDetail);
                    NotificationManager.DisplayWarningNotification("SPSave: the save didn't check out, so it was discarded. Your raid continues.");
                    return;
                }

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

        private static IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(2f);
            Plugin.Log.LogInfo("Closing the game after Save & Quit.");
            Application.Quit();
        }
    }
}
