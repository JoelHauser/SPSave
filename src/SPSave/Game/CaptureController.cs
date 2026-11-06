using System;
using System.Diagnostics;
using System.IO;
using EFT;
using EFT.Communications;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Step A only: a hotkey that captures the raid to disk, reads it back and reports timings. It restores nothing
    /// and never ends the raid. Its Update is the mod's only per-frame code: one key check (the shipped mod will use
    /// a pause-menu button instead and have none).
    /// </summary>
    internal sealed class CaptureController : MonoBehaviour
    {
        internal static void Attach(GameWorld world)
        {
            if (world.GetComponent<CaptureController>() == null)
            {
                world.gameObject.AddComponent<CaptureController>();
            }
        }

        private void Update()
        {
            if (Plugin.CaptureKey.Value.IsDown())
            {
                Capture();
            }
        }

        private static void Capture()
        {
            var frame = Stopwatch.StartNew();
            try
            {
                var result = RaidCapture.Run(Plugin.CombatWindowSeconds.Value);
                var report = result.Report;

                var path = Path.Combine(Plugin.SaveRoot, "saves", result.ProfileId, "raid.spsave");
                var write = SnapshotFile.Write(path, result.Sections);
                report.RawBytes = write.RawBytes;
                report.FileBytes = write.FileBytes;
                report.EncodeMs = write.EncodeMs;
                report.WriteMs = write.WriteMs + write.ReplaceMs;
                report.VerifyFileMs = write.VerifyMs;

                if (Plugin.VerifyRoundTrip.Value)
                {
                    var watch = Stopwatch.StartNew();
                    var check = RoundTrip.Verify(SnapshotFile.Read(path), result.WorldItemIds);
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
                if (report.RoundTripOk)
                {
                    NotificationManager.DisplayMessageNotification(
                        $"SPSave: captured in {report.FrameMs:F0} ms, {report.FileBytes / 1024} KB" + (report.InCombat ? " (in combat)" : ""));
                }
                else
                {
                    Plugin.Log.LogWarning("Round trip failed: " + report.RoundTripDetail);
                    NotificationManager.DisplayWarningNotification("SPSave: captured, but the round trip failed. See the BepInEx log.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Capture failed: " + e);
                NotificationManager.DisplayWarningNotification("SPSave: capture failed. See the BepInEx log.");
            }
        }
    }
}
