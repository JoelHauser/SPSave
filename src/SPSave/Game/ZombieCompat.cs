using System;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    /// <summary>
    /// Halloween infection (zombies; off unless SPT's seasonalevents.json enables zombieSettings). The game's
    /// BotHalloweenWithZombies fires one extra round of zombie spawns (SpawnAction) when the raid clock passes half the
    /// session (CheckMidSessionTriggerBotsSpawn), and keeps only a bool (_midSessionSpawned) to say it did. On a resume
    /// that bool starts false while the clock starts at the time played, so a save made after the half point would get
    /// the round a second time as soon as the raid starts. After Activate on a resume, the bool says whether the saved
    /// raid had already passed the half point. Restored zombies need nothing here: Activate subscribes every bot already
    /// in the list and OnBotAdd every later one, which is how BotRestore's bots arrive. Their pursuits and crowd spawns
    /// are live state and start fresh, like every restored bot's awareness. Not checked in game (Joel, 2026-11-07).
    /// </summary>
    internal sealed class ZombieMidSessionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotHalloweenWithZombies), nameof(BotHalloweenWithZombies.Activate));

        [PatchPostfix]
        private static void Postfix(BotHalloweenWithZombies __instance)
        {
            try
            {
                var clock = ResumeSession.State?.Clock;
                if (!ResumeSession.Active || clock == null || clock.SessionSeconds <= 0)
                {
                    return;
                }
                var passed = clock.PastSeconds >= clock.SessionSeconds * 0.5;
                __instance._midSessionSpawned = passed;
                Plugin.Log.LogInfo(passed
                    ? $"Zombies: the saved raid was past half time ({clock.PastSeconds:F0} of {clock.SessionSeconds:F0} s), so the mid-raid zombie spawns don't run again."
                    : $"Zombies: the mid-raid zombie spawns still come at half time ({clock.SessionSeconds * 0.5:F0} s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Zombie mid-raid spawn check failed: " + e.Message);
            }
        }
    }
}
