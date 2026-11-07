using System;
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Finishes a resume once PreloaderUI's black overlay has faded: uses up the save, unlocks the profile and puts the
    /// raid clock and timer panels right. Joel (2026-11-06): no Ready window, no key, straight back into the raid.
    /// Nothing is held during the fade, as in a normal raid start: 0.8.5 still froze the player (Freeze, removed in
    /// 0.8.6), which left the arms stuck in a pose and took control away for a moment after (Joel: not polished).
    /// Exists only for the fade; destroyed after, so it costs nothing during play. Timer handling follows the Pause
    /// mod (netVnum.Pause): the raid timer and panels are shifted by the time the fade took, as when it was frozen.
    /// </summary>
    internal sealed class ReadyController : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<BaseLocalGame<EftGamePlayerOwner>, DateTime> SessionStartTime =
            AccessTools.FieldRefAccess<BaseLocalGame<EftGamePlayerOwner>, DateTime>("SessionStartTime");

        private static readonly AccessTools.FieldRef<TimerPanel, DateTime> TimerPanelEnd =
            AccessTools.FieldRefAccess<TimerPanel, DateTime>("_dateTime");

        /// <summary>PreloaderUI's black overlay; the resume is finished once it is gone.</summary>
        private static readonly FieldInfo BlackOverlay = AccessTools.Field(typeof(PreloaderUI), "_overlapBlackImage");

        private DateTime _startedAt;

        internal static void Attach(GameWorld world)
        {
            if (world != null && world.GetComponent<ReadyController>() == null)
            {
                world.gameObject.AddComponent<ReadyController>();
            }
        }

        private void Start()
        {
            _startedAt = DateTimeExtensions.UtcNow;
            ResumeSession.Phase = ResumePhase.Finishing;
            PitFireTeamCompat.PlaceSquad(ResumeSession.State);
            StartCoroutine(ReadyAfterFadeIn());
        }

        private IEnumerator ReadyAfterFadeIn()
        {
            var giveUpAt = Time.realtimeSinceStartup + 8f;
            while (BlackScreenShowing() && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
            // A frame later, not inside whatever ended the fade.
            yield return null;
            if (ResumeSession.Phase == ResumePhase.Finishing)
            {
                Ready();
            }
        }

        private static bool BlackScreenShowing()
        {
            var ui = MonoBehaviourSingleton<PreloaderUI>.Instance;
            var overlay = ui != null ? BlackOverlay?.GetValue(ui) as Component : null;
            return overlay != null && overlay.gameObject.activeInHierarchy;
        }

        private void Ready()
        {
            // Time from the game start to the end of the fade; the clock is shifted by it, so the time left is what was saved.
            var faded = DateTimeExtensions.UtcNow - _startedAt;
            var game = Singleton<AbstractGame>.Instance as BaseLocalGame<EftGamePlayerOwner>;
            var timer = game?.GameTimer;
            var clock = ResumeSession.State?.Clock;

            // Each step on its own: a failure in one (the first run hit an empty extract timer) must not skip the
            // rest. Using up the save goes first; it is the one that matters most.
            Step("using up the save", () =>
            {
                var retired = SnapshotFile.Retire(ResumeSession.SavePath, DateTime.UtcNow);
                Plugin.Log.LogInfo($"Save used up and moved to {retired}.");
            });
            Step("unlocking the profile", () =>
            {
                Plugin.Log.LogInfo(LockClient.Unlock() ? "Profile unlocked on the server." : "The server didn't confirm the unlock; it unlocks at raid end.");
            });
            Step("found-in-raid check", () =>
            {
                RaidCapture.CheckInventory("Taking control", ResumeSession.State?.Player?.Items, ResumeSession.Profile);
            });
            Step("raid timer", () =>
            {
                if (timer?._startDateTime != null && timer._escapeDateTime != null)
                {
                    timer._startDateTime = timer._startDateTime.Value + faded;
                    timer._escapeDateTime = timer._escapeDateTime.Value + faded;
                }
            });
            Step("timer panels", () =>
            {
                // The panels were set when the game started (ExtractionTimersPanel.SetTime) from the full raid
                // length, not the time left: the main timer gets the raid's real end; each extract timer, set to
                // now + its own time, loses the time already played and gains the fade time. Panels that
                // were never shown have no end time (DateTime.MinValue) and are left alone.
                var shift = faded - TimeSpan.FromSeconds(clock?.PastSeconds ?? 0d);
                foreach (var panel in FindObjectsOfType<TimerPanel>(true))
                {
                    var end = TimerPanelEnd(panel);
                    if (end.Year < 2000)
                    {
                        continue;
                    }
                    if (panel is MainTimerPanel && timer?._escapeDateTime != null)
                    {
                        TimerPanelEnd(panel) = timer._escapeDateTime.Value;
                    }
                    else if (panel is ExitTimerPanel)
                    {
                        TimerPanelEnd(panel) = end + shift;
                    }
                }
            });
            Step("session start", () =>
            {
                if (game != null && clock != null)
                {
                    SessionStartTime(game) = DateTimeExtensions.Now - TimeSpan.FromSeconds(clock.PastSeconds);
                }
            });
            Step("time of day", () =>
            {
                var world = Singleton<GameWorld>.Instance;
                if (world != null)
                {
                    RaidRestore.RestoreTimeOfDay(world, clock);
                }
            });

            var left = timer?._escapeDateTime != null ? (timer._escapeDateTime.Value - DateTimeExtensions.UtcNow).TotalSeconds : 0d;
            Plugin.Log.LogInfo($"Resume finished {faded.TotalSeconds:F1} s after the raid started (loading fade); {left:F0} s left of {timer?.SessionSeconds()}.");
            ResumeSession.GoLive();
            Destroy(this);
        }

        private static void Step(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Finishing the resume: {what} failed: {e}");
            }
        }

        private void OnDestroy()
        {
            if (ResumeSession.Phase == ResumePhase.Finishing)
            {
                // The raid closed during the fade: the save was not used up, so it can be resumed again.
                ResumeSession.Reset();
            }
        }
    }
}
