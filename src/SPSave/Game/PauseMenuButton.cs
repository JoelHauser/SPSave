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
                var show = minimized && CaptureController.Current != null && !CaptureController.Quitting &&
                           ResumeSession.Phase != ResumePhase.WaitingForReady && ResumeSession.Phase != ResumePhase.Resolving;
                var button = Find(__instance, create: show);
                if (button != null)
                {
                    button.gameObject.SetActive(show);
                }
                // A resumed raid still frozen before Ready: LEAVE would end it as Left through GameEnd and send the
                // saved profile to the server (local/end), although the save isn't used up until Ready (DESIGN.md).
                // The way out before Ready is Ready, or closing the game, which keeps the save and the lock.
                if (minimized && ResumeSession.Phase == ResumePhase.WaitingForReady && __instance._disconnectButton != null)
                {
                    __instance._disconnectButton.gameObject.SetActive(false);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Pause menu button failed: " + e);
            }
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
            // Above LEAVE: the destructive choice stays where it was.
            clone.transform.SetSiblingIndex(leave.transform.GetSiblingIndex());
            var button = clone.GetComponent<DefaultUIButton>();
            button.SetRawText(Label, leave.HeaderSize);
            button.OnClick.AddListener(() => CaptureController.Current?.RequestFromMenu());
            Plugin.Log.LogInfo("Added SAVE & QUIT to the pause menu.");
            return button;
        }
    }
}
