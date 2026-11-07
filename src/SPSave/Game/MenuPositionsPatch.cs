using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Before MenuScreen.Show's postfixes (Menu Overhaul's layout, Pit FireTeam's MY SQUAD): see
    /// PitFireTeamCompat.ForgetMenuPositions. On MenuScreen.Show(controller), which calls Show(profile, ...), because the
    /// controller says which menu this is; MenuScreen._reconnectAvailable is set only after Show (in ShowAction), so it
    /// still read "reconnect" on the first menu after a resumed raid.
    /// </summary>
    internal sealed class MenuPositionsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.Show), new[] { typeof(MenuScreen.MainMenuBaseScreenController) });

        [PatchPrefix]
        private static void Prefix(MenuScreen __instance, MenuScreen.MainMenuBaseScreenController controller)
        {
            try
            {
                // The normal main menu only: not the reconnect-mode menu (Menu Overhaul skips that layout) or the pause menu.
                if (controller is MenuScreen.MainMenuScreenController)
                {
                    PitFireTeamCompat.ForgetMenuPositions(__instance);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clearing Pit FireTeam's menu record failed: " + e.Message);
            }
        }

        /// <summary>
        /// After every other mod's postfix (Menu Overhaul restyles the buttons there): MY SQUAD gets CHARACTER's
        /// brush state, now and a frame later. See PitFireTeamCompat.MatchSquadButtonBrush.
        /// </summary>
        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MenuScreen __instance, MenuScreen.MainMenuBaseScreenController controller)
        {
            try
            {
                if (controller is MenuScreen.MainMenuScreenController && __instance != null && __instance.isActiveAndEnabled)
                {
                    PitFireTeamCompat.MatchSquadButtonBrush(__instance);
                    __instance.StartCoroutine(MatchNextFrame(__instance));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Matching MY SQUAD to Menu Overhaul's buttons failed: " + e.Message);
            }
        }

        private static IEnumerator MatchNextFrame(MenuScreen screen)
        {
            yield return null;
            try
            {
                PitFireTeamCompat.MatchSquadButtonBrush(screen);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Matching MY SQUAD to Menu Overhaul's buttons failed: " + e.Message);
            }
        }
    }
}
