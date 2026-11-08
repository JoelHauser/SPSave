using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    /// <summary>
    /// Raid Review (ekky.raidreview) records a raid from GameWorld.OnGameStarted to Player.OnGameSessionEnd, timing
    /// everything with one Stopwatch (RAID_REVIEW.stopwatch) that its OnGameStarted postfix resets and starts. Each
    /// start sends START, and its server opens a new raid. A save therefore left the first half unfinished (Raid
    /// Review's cleanup deletes those) and recorded the resume as a new raid from 0 s (2026-11-08).
    ///
    /// Both halves are one raid now: the server mod reopens the saved raid at the resume's START
    /// (SPSave.Server.RaidReviewContinuation), and here the save keeps the stopwatch's reading and a resume sets the
    /// stopwatch on by that much right after Raid Review starts it, so the second half's times follow on.
    /// Members checked against Raid Review 1.9.0; Stopwatch.elapsed is Mono's (EFT's System.dll).
    /// </summary>
    internal static class RaidReviewCompat
    {
        internal const string Guid = "ekky.raidreview";

        private static bool _looked;
        private static FieldInfo _stopwatch;
        private static FieldInfo _inRaid;
        private static readonly FieldInfo Elapsed = AccessTools.Field(typeof(Stopwatch), "elapsed");

        internal static Assembly Assembly =>
            Chainloader.PluginInfos.TryGetValue(Guid, out var info) && info.Instance != null ? info.Instance.GetType().Assembly : null;

        private static void Look()
        {
            if (_looked)
            {
                return;
            }
            _looked = true;
            var main = Assembly?.GetType("RAID_REVIEW.RAID_REVIEW");
            _stopwatch = main != null ? AccessTools.Field(main, "stopwatch") : null;
            _inRaid = main != null ? AccessTools.Field(main, "inRaid") : null;
            if (main != null && (_stopwatch == null || _inRaid == null || Elapsed == null))
            {
                Plugin.Log.LogWarning("Raid Review is loaded but its recording clock was not found; a resumed raid's times start at 0 s.");
            }
        }

        private static Stopwatch Recording()
        {
            Look();
            if (_stopwatch == null || _inRaid == null || !(_inRaid.GetValue(null) is bool inRaid) || !inRaid)
            {
                return null;
            }
            return _stopwatch.GetValue(null) as Stopwatch;
        }

        /// <summary>Raid Review's recording time so far, for the save; null when it isn't recording.</summary>
        internal static long? ReadElapsedMs()
        {
            try
            {
                return Recording()?.ElapsedMilliseconds;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void Enable()
        {
            if (Assembly == null)
            {
                return;
            }
            try
            {
                new RaidReviewStartPatch().Enable();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Raid Review is loaded but its raid start was not found; a resumed raid's times start at 0 s. " + e.Message);
            }
        }

        /// <summary>After Raid Review started its stopwatch on a resume: carry on from the saved reading.</summary>
        internal static void CarryOnClock()
        {
            var savedMs = ResumeSession.State?.RaidReviewElapsedMs ?? 0;
            if (!ResumeSession.Active || savedMs <= 0 || Elapsed == null)
            {
                return;
            }
            try
            {
                var watch = Recording();
                if (watch == null || !watch.IsRunning || watch.ElapsedMilliseconds >= savedMs)
                {
                    return;
                }
                var ticks = savedMs * Stopwatch.Frequency / 1000;
                Elapsed.SetValue(watch, (long)Elapsed.GetValue(watch) + ticks);
                Plugin.Log.LogInfo($"Raid Review: recording carries on from {savedMs / 1000.0:F0} s, the saved raid's time.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Raid Review's recording clock could not be set; this half's times start at 0 s. " + e.Message);
            }
        }
    }

    /// <summary>
    /// RAID_REVIEW_Player_OnGameStartedPatch.PatchPostFix: Raid Review's own postfix on GameWorld.OnGameStarted, which
    /// resets and starts its stopwatch. Patching it (not OnGameStarted) makes this run right after it.
    /// </summary>
    internal sealed class RaidReviewStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var type = RaidReviewCompat.Assembly?.GetType("RAID_REVIEW.RAID_REVIEW_Player_OnGameStartedPatch");
            return type != null ? AccessTools.Method(type, "PatchPostFix") : null;
        }

        [PatchPostfix]
        private static void Postfix() => RaidReviewCompat.CarryOnClock();
    }
}
