using System;
using System.Collections;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPSave.Core;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Ends a resumed raid at once as Killed or MIA (Abandon, a combat save) through the game's own exits, so
    /// Player.OnGameSessionEnd runs: session XP into the level, death/MIA counters, quest exit and fail conditions.
    /// The save holds the profile from before all of that; sending it to the server as is would skip it (C1,
    /// docs/C-SERVER-API.md).
    /// Killed: ActiveHealthController.Kill raises DiedEvent, which runs the player's death handling and
    /// BaseLocalGame.InitiateGameStopping (death panel, Stop(Killed, 5 s)), as any death does.
    /// MIA: Stop(MissingInAction), the call the raid timer makes when it runs out.
    /// Takes the place of ReadyController: the world is not frozen, and the save is used up before the raid ends.
    /// </summary>
    internal sealed class ResolveController : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<BaseLocalGame<EftGamePlayerOwner>, DateTime> SessionStartTime =
            AccessTools.FieldRefAccess<BaseLocalGame<EftGamePlayerOwner>, DateTime>("SessionStartTime");

        internal static void Attach(GameWorld world)
        {
            if (world != null && world.GetComponent<ResolveController>() == null)
            {
                world.gameObject.AddComponent<ResolveController>();
            }
        }

        private void Start()
        {
            ResumeSession.Phase = ResumePhase.Resolving;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // One frame, so this isn't issued from inside the game's start. The screen stays black throughout
            // (ResolveBlackout); the raid is never shown.
            yield return null;
            End();
            Destroy(this);
        }

        private static void End()
        {
            var outcome = ResumeSession.Outcome;
            var game = Singleton<AbstractGame>.Instance as BaseLocalGame<EftGamePlayerOwner>;
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (game == null || player == null || outcome == null)
            {
                Plugin.Log.LogError($"Can't end the saved raid: game {game != null}, player {player != null}, outcome {outcome}. The save is kept.");
                // Not left black with no way out: show the raid; the player can Leave from the menu.
                ResumeSession.Reset();
                MonoBehaviourSingleton<PreloaderUI>.Instance?.FadeBlackScreen(0.5f, -0.3f);
                return;
            }

            // First, as on Ready: the save is used up whatever happens next.
            try
            {
                var retired = SnapshotFile.Retire(ResumeSession.SavePath, DateTime.UtcNow);
                Plugin.Log.LogInfo($"Save used up ({outcome}) and moved to {retired}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Using up the save failed: " + e);
            }

            // playTime and the death panel's time count from SessionStartTime: include the time played before.
            var clock = ResumeSession.State?.Clock;
            if (clock != null)
            {
                SessionStartTime(game) = DateTimeExtensions.Now - TimeSpan.FromSeconds(clock.PastSeconds);
            }

            var map = ResumeSession.Manifest?.Map;
            ResumeSession.GoLive();
            Plugin.Log.LogInfo($"Ending the saved raid on {map} as {outcome}.");
            if (outcome == ExitStatus.Killed)
            {
                player.ActiveHealthController.Kill(EDamageType.Undefined);
            }
            else
            {
                game.Stop(player.ProfileId, ExitStatus.MissingInAction, null);
            }
        }
    }
}
