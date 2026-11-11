using System.Collections;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// SAVE &amp; QUIT in the in-raid pause menu (Escape). The in-raid menu is MenuScreen in its minimized layout
    /// (RaidMainMenuScreenController → ShowInRaid → ChangeScreenInGameStatus(true)), which shows only the stock LEAVE
    /// (_disconnectButton) and the hide button. The new button is a clone of LEAVE, so its font, frame, hover and
    /// sound are the game's own (Joel: every piece of UI must look like stock EFT); only its label is ours.
    /// DefaultUIButton.OnClick is a per-instance event that Unity doesn't serialize, so the clone gets none of LEAVE's
    /// listeners.
    /// </summary>
    internal sealed class PauseMenuButtonPatch : ModulePatch
    {
        private const string ObjectName = "SPSaveSaveAndQuit";
        private const string Label = "Save & quit";

        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.ChangeScreenInGameStatus));

        [PatchPostfix]
        private static void Postfix(MenuScreen __instance, bool minimized)
        {
            try
            {
                var show = minimized && CaptureController.Current != null && !CaptureController.Quitting && !CaptureController.ScavRaid &&
                           ResumeSession.Phase != ResumePhase.Finishing && ResumeSession.Phase != ResumePhase.Resolving;
                var button = Find(__instance, create: show);
                if (button != null)
                {
                    button.gameObject.SetActive(show);
                    Arrange(__instance, button, show);
                }
                // A resume not yet finished (the loading fade): LEAVE would end it as Left through GameEnd and send the
                // saved profile to the server (local/end), although the save isn't used up yet (DESIGN.md). It is
                // only the length of the fade; closing the game then keeps the save and the lock.
                if (minimized && ResumeSession.Phase == ResumePhase.Finishing && __instance._disconnectButton != null)
                {
                    __instance._disconnectButton.gameObject.SetActive(false);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Pause menu button failed: " + e);
            }
        }

        /// <summary>
        /// The pause menu's parent doesn't stack its children (0.8.0's clone landed exactly on LEAVE), and LEAVE sits
        /// at the top of the screen, so SAVE &amp; QUIT goes one stock step below LEAVE, where RESUME
        /// (_hideScreenButton) was, and RESUME moves down by that same step. The step is the game's own LEAVE→RESUME
        /// distance, so the column keeps the stock spacing. LEAVE stays where it was.
        /// Pit FireTeam adds SQUAD SETTINGS one row under RESUME (its SquadControlMenuUi); it becomes the column's fourth
        /// row (see Targets). With Pit FireTeam, 0.9.1 showed
        /// RESUME back under SAVE &amp; QUIT (cause unknown), so LayoutKeeper checks again after opening and logs
        /// anything that moved.
        /// All four buttons share MenuScreen as parent, so the arithmetic is in its local space (canvas units), and
        /// RESUME's home is its own anchoredPosition. 1.0.1 kept world positions, which go stale when the screen size
        /// changes: after a switch from 3440x1440 to 2560x1440 world units became canvas units, SAVE &amp; QUIT landed
        /// far right of LEAVE and RESUME off screen (2026-11-10).
        /// </summary>
        internal static void Arrange(MenuScreen screen, DefaultUIButton button, bool show)
        {
            var resume = screen._hideScreenButton != null ? screen._hideScreenButton.transform as RectTransform : null;
            var home = button.GetComponent<ResumeHome>();
            if (resume == null || home == null)
            {
                return;
            }
            var pit = PitButton(resume);
            if (!show)
            {
                resume.anchoredPosition = home.Anchored;
                if (pit != null)
                {
                    // Pit FireTeam's own place for it: RESUME's anchoredPosition minus RESUME's height + 5.
                    pit.anchoredPosition = resume.anchoredPosition - new Vector2(0f, resume.rect.height + 5f);
                }
                return;
            }
            var targets = Targets(screen, home, resume, pit);
            button.transform.localPosition = targets.SaveQuit;
            resume.localPosition = targets.Resume;
            if (pit != null)
            {
                pit.localPosition = targets.Pit;
            }
        }

        private struct Layout
        {
            public Vector3 SaveQuit;
            public Vector3 Resume;
            public Vector3 Pit;
        }

        /// <summary>
        /// One column under LEAVE, evenly spaced, in the parent's local space. Without Pit FireTeam the step is the
        /// game's own LEAVE→RESUME distance. With its SQUAD SETTINGS as a fourth row the step is halfway between that and
        /// Pit FireTeam's own RESUME→SQUAD SETTINGS step (RESUME's height + 5), so the four rows are evenly spaced (Joel:
        /// 0.9.2's 140/140/80 looked off).
        /// </summary>
        private static Layout Targets(MenuScreen screen, ResumeHome home, RectTransform resume, RectTransform pit)
        {
            var leave = screen._disconnectButton.transform.localPosition;
            // RESUME's local position at home: an anchoredPosition shift moves localPosition by the same amount.
            var resumeHome = resume.localPosition + (Vector3)(home.Anchored - resume.anchoredPosition);
            var stock = resumeHome - leave;
            var step = stock;
            if (pit != null && stock.sqrMagnitude > 0.01f)
            {
                var pitStep = resume.rect.height + 5f;
                step = stock.normalized * ((stock.magnitude + pitStep) / 2f);
            }
            return new Layout { SaveQuit = leave + step, Resume = leave + 2f * step, Pit = leave + 3f * step };
        }

        private const string PitButtonName = "pitFireTeam_RaidSettingsButton";

        private static RectTransform PitButton(RectTransform resume)
        {
            return resume.transform.parent != null ? resume.transform.parent.Find(PitButtonName) as RectTransform : null;
        }

        /// <summary>
        /// On the SAVE &amp; QUIT clone: each time the menu opens, checks a frame later and again after 0.3 s that RESUME
        /// is where Arrange put it, puts it back if not and logs what it found. Runs only while the pause menu opens.
        /// </summary>
        private sealed class LayoutKeeper : MonoBehaviour
        {
            internal MenuScreen Screen;
            private bool _reported;

            private void OnEnable()
            {
                if (Screen != null)
                {
                    StartCoroutine(Check());
                }
            }

            private IEnumerator Check()
            {
                yield return null;
                Verify("a frame after opening");
                yield return new WaitForSecondsRealtime(0.3f);
                Verify("0.3 s after opening");
            }

            private void Verify(string when)
            {
                var resume = Screen != null && Screen._hideScreenButton != null ? Screen._hideScreenButton.transform as RectTransform : null;
                var button = GetComponent<DefaultUIButton>();
                var home = GetComponent<ResumeHome>();
                if (resume == null || button == null || home == null || !gameObject.activeInHierarchy)
                {
                    return;
                }
                var pit = PitButton(resume);
                var expected = Targets(Screen, home, resume, pit);
                if ((resume.localPosition - expected.Resume).sqrMagnitude < 1f &&
                    (transform.localPosition - expected.SaveQuit).sqrMagnitude < 1f &&
                    (pit == null || (pit.localPosition - expected.Pit).sqrMagnitude < 1f))
                {
                    return;
                }
                if (!_reported)
                {
                    _reported = true;
                    Plugin.Log.LogWarning($"Pause menu: something moved the buttons ({when}): RESUME at {resume.localPosition} " +
                                          $"(expected {expected.Resume}), SAVE & QUIT at {transform.localPosition} (expected {expected.SaveQuit})" +
                                          (pit != null ? $", SQUAD SETTINGS at {pit.localPosition} (expected {expected.Pit})" : "") + ". Put back.");
                }
                Arrange(Screen, button, show: true);
            }
        }

        private static void LogLayout(DefaultUIButton leave, DefaultUIButton resume)
        {
            static string Describe(Component c)
            {
                if (c == null)
                {
                    return "none";
                }
                var rect = c.transform as RectTransform;
                var parent = c.transform.parent;
                var group = parent != null ? parent.GetComponent<UnityEngine.UI.LayoutGroup>() : null;
                return c.name + " under " + (parent != null ? parent.name : "-") +
                       (group != null ? " (" + group.GetType().Name + ")" : " (no layout group)") +
                       " at " + c.transform.localPosition + (rect != null ? " size " + rect.rect.size : "");
            }
            Plugin.Log.LogInfo("Pause menu layout: LEAVE " + Describe(leave) + "; RESUME " + Describe(resume));
        }

        /// <summary>RESUME's stock anchoredPosition, kept on the clone so moving it never accumulates. Unlike a world
        /// position it holds when the screen size changes.</summary>
        private sealed class ResumeHome : MonoBehaviour
        {
            public Vector2 Anchored;
        }

        private static DefaultUIButton Find(MenuScreen screen, bool create)
        {
            var leave = screen._disconnectButton;
            if (leave == null)
            {
                return null;
            }
            var parent = leave.transform.parent;
            var existing = parent.Find(ObjectName);
            if (existing != null)
            {
                return existing.GetComponent<DefaultUIButton>();
            }
            if (!create)
            {
                return null;
            }
            var clone = Object.Instantiate(leave.gameObject, parent);
            clone.name = ObjectName;
            clone.transform.SetSiblingIndex(leave.transform.GetSiblingIndex() + 1);
            var resume = screen._hideScreenButton;
            if (resume != null && resume.transform is RectTransform resumeRect)
            {
                clone.AddComponent<ResumeHome>().Anchored = resumeRect.anchoredPosition;
            }
            clone.AddComponent<LayoutKeeper>().Screen = screen;
            LogLayout(leave, resume);
            var button = clone.GetComponent<DefaultUIButton>();
            button.SetRawText(Label, leave.HeaderSize);
            button.OnClick.AddListener(() => CaptureController.Current?.RequestFromMenu());
            Plugin.Log.LogInfo("Added SAVE & QUIT to the pause menu.");
            return button;
        }
    }
}
