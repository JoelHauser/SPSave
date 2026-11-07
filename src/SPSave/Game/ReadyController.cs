using System;
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Holds a restored raid frozen (see Freeze) until the player presses the Ready key, then gives them control
    /// with the clock exactly where it was saved. Exists only while waiting; it is destroyed on Ready, so it costs
    /// nothing during play. Timer handling follows the Pause mod (netVnum.Pause): shift the raid timer and the timer
    /// panel by the time spent frozen.
    /// </summary>
    internal sealed class ReadyController : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<BaseLocalGame<EftGamePlayerOwner>, DateTime> SessionStartTime =
            AccessTools.FieldRefAccess<BaseLocalGame<EftGamePlayerOwner>, DateTime>("SessionStartTime");

        private static readonly AccessTools.FieldRef<TimerPanel, DateTime> TimerPanelEnd =
            AccessTools.FieldRefAccess<TimerPanel, DateTime>("_dateTime");

        /// <summary>PreloaderUI's black overlay; its fade-in steps by Time.deltaTime, so game time stops only after it.</summary>
        private static readonly FieldInfo BlackOverlay = AccessTools.Field(typeof(PreloaderUI), "_overlapBlackImage");

        private const string PromptText = "Your raid is restored and paused. Continue when you're ready.";

        private DateTime _frozenAt;
        private DialogWindowContext _prompt;
        private bool _readyRequested;

        internal static void Attach(GameWorld world)
        {
            if (world != null && world.GetComponent<ReadyController>() == null)
            {
                world.gameObject.AddComponent<ReadyController>();
            }
        }

        private void Start()
        {
            _frozenAt = DateTimeExtensions.UtcNow;
            ResumeSession.Phase = ResumePhase.WaitingForReady;
            Freeze.Begin(Singleton<GameWorld>.Instance?.MainPlayer);
            StartCoroutine(StopTimeAfterFadeIn());
        }

        private IEnumerator StopTimeAfterFadeIn()
        {
            var giveUpAt = Time.realtimeSinceStartup + 8f;
            while (BlackScreenShowing() && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
            if (ResumeSession.Phase != ResumePhase.WaitingForReady)
            {
                yield break;
            }
            // The stock message window (Joel: UI must look like stock EFT), opened before time stops so its own
            // opening animation can play. The world is already held by Freeze.
            ShowPrompt();
            yield return new WaitForSecondsRealtime(0.6f);
            if (ResumeSession.Phase == ResumePhase.WaitingForReady)
            {
                Time.timeScale = 0f;
            }
        }

        private void ShowPrompt()
        {
            try
            {
                _prompt = ItemUiContext.Instance?.ShowMessageWindow(PromptText,
                    () => _readyRequested = true,
                    // Closed without accepting (Escape): ask again, the raid stays paused.
                    () => { if (this != null && !_readyRequested) StartCoroutine(PromptAgain()); },
                    "READY".Localized());
                if (_prompt == null)
                {
                    NotificationManager.DisplayMessageNotification($"SPSave: raid restored. Press {Plugin.ReadyKey.Value} to take control.",
                        ENotificationDurationType.Long);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Showing the Ready prompt failed; the Ready key still works: " + e);
                NotificationManager.DisplayMessageNotification($"SPSave: raid restored. Press {Plugin.ReadyKey.Value} to take control.",
                    ENotificationDurationType.Long);
            }
        }

        private IEnumerator PromptAgain()
        {
            yield return null;
            if (ResumeSession.Phase == ResumePhase.WaitingForReady && !_readyRequested)
            {
                ShowPrompt();
            }
        }

        private static bool BlackScreenShowing()
        {
            var ui = MonoBehaviourSingleton<PreloaderUI>.Instance;
            var overlay = ui != null ? BlackOverlay?.GetValue(ui) as Component : null;
            return overlay != null && overlay.gameObject.activeInHierarchy;
        }

        private void Update()
        {
            // The prompt's accept sets the flag; Ready runs here, a frame later, not inside the window's callback.
            if (_readyRequested || Plugin.ReadyKey.Value.IsDown())
            {
                _readyRequested = true;
                _prompt?.CloseSilent();
                _prompt = null;
                Ready();
            }
        }

        private void Ready()
        {
            var frozen = DateTimeExtensions.UtcNow - _frozenAt;
            Time.timeScale = 1f;
            Step("unfreeze", Freeze.End);
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
                    timer._startDateTime = timer._startDateTime.Value + frozen;
                    timer._escapeDateTime = timer._escapeDateTime.Value + frozen;
                }
            });
            Step("timer panels", () =>
            {
                // The panels were set when the game started (ExtractionTimersPanel.SetTime) from the full raid
                // length, not the time left: the main timer gets the raid's real end; each extract timer, set to
                // now + its own time, loses the time already played and gains the time spent frozen. Panels that
                // were never shown have no end time (DateTime.MinValue) and are left alone.
                var shift = frozen - TimeSpan.FromSeconds(clock?.PastSeconds ?? 0d);
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
            Plugin.Log.LogInfo($"Player took control after {frozen.TotalSeconds:F0} s frozen; {left:F0} s left of {timer?.SessionSeconds()}.");
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
            _prompt?.CloseSilent();
            _prompt = null;
            if (ResumeSession.Phase == ResumePhase.WaitingForReady)
            {
                // The raid closed while still frozen (Leave from the menu): don't leave the game paused. The save
                // was not used up, so it can be resumed again.
                Time.timeScale = 1f;
                Freeze.End();
                ResumeSession.Reset();
            }
        }
    }
}
