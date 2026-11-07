using System;
using System.Collections;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// A save being ended as Killed or MIA must never show the raid (Joel, 0.6.0 test: the world was visible and the
    /// player could move before the fade to black). From the game's start (Begin, in GameStartPatch) until the game's
    /// own raid end takes over, PreloaderUI's black overlay stays fully black.
    /// Every fade runs through PreloaderUI.Co_ShowBlackImage, so that is the one patch:
    /// - fades towards clear (the raid start's FadeBlackScreen(0.5, -0.3), including its delayed start) stay black
    ///   and never finish;
    /// - fades to black (Stop's StartBlackScreenShow(1, 1, cb), whose coroutine starts at alpha 0 and would show the
    ///   world for a second) stay black and run cb one frame later; that callback is GameEnd, and the hold ends there.
    /// After that the menu-side fades (TarkovApplication.OnGameEnd) run as usual.
    /// </summary>
    internal static class ResolveBlackout
    {
        internal static bool Holding { get; private set; }

        private static readonly FieldInfo FadeCoroutine = AccessTools.Field(typeof(PreloaderUI), "_showBlackImageCoroutine");

        internal static void Enable()
        {
            new ShowBlackImagePatch().Enable();
        }

        /// <summary>Black now, and any fade already running (the raid start's) is stopped.</summary>
        internal static void Begin()
        {
            Holding = true;
            var ui = MonoBehaviourSingleton<PreloaderUI>.Instance;
            if (ui == null)
            {
                return;
            }
            if (FadeCoroutine?.GetValue(ui) is Coroutine running)
            {
                ui.StopCoroutine(running);
                FadeCoroutine.SetValue(ui, null);
            }
            ui.SetBlackImageAlpha(1f);
        }

        internal static void End()
        {
            Holding = false;
        }

        private static IEnumerator StayBlack(PreloaderUI ui, float to, Action callback)
        {
            ui.SetBlackImageAlpha(1f);
            if (to < 1f)
            {
                // A reveal: never finishes while holding (its callback would hide the overlay).
                while (Holding)
                {
                    ui.SetBlackImageAlpha(1f);
                    yield return null;
                }
                yield break;
            }
            yield return null;
            Holding = false;
            Plugin.Log.LogInfo("Raid end taking over from the blackout.");
            callback?.Invoke();
        }

        private sealed class ShowBlackImagePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.Co_ShowBlackImage));

            [PatchPrefix]
            private static bool Prefix(PreloaderUI __instance, float to, Action callback, ref IEnumerator __result)
            {
                if (!Holding)
                {
                    return true;
                }
                __result = StayBlack(__instance, to, callback);
                return false;
            }
        }
    }
}
