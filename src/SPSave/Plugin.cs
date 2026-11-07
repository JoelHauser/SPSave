using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SPSave.Core;
using SPSave.Game;
using UnityEngine;

namespace SPSave
{
    /// <summary>What Abandon or a combat save does to living Pit FireTeam squadmates (Joel: the player chooses).</summary>
    public enum TeammateFate
    {
        ShareYourFate,
        ComeHomeSafe
    }

    [BepInPlugin(Guid, "SPSave", Version)]
    [BepInDependency("com.SPT.core")]
    // Optional; loaded first so their types and patches are in place. See docs/B2-COMPAT.md.
    [BepInDependency(SainCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AbpsCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.chazut.orbit", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.joelhauser.spsave";
        public const string Version = "0.9.7";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> CombatWindowSeconds;
        internal static ConfigEntry<bool> VerifyRoundTrip;
        internal static ConfigEntry<bool> ResumeEnabled;
        internal static ConfigEntry<TeammateFate> PitTeammateFate;

        /// <summary>&lt;game root&gt;\SPSave: saves\&lt;profile id&gt;\raid.spsave and captures.csv.</summary>
        internal static string SaveRoot => Path.Combine(BepInEx.Paths.GameRootPath, "SPSave");

        internal static string SavePath(string profileId) => Path.Combine(SaveRoot, "saves", profileId, "raid.spsave");

        private void Awake()
        {
            Log = Logger;
            CombatWindowSeconds = Config.Bind("Step A", "Combat window (seconds)", 30f,
                "A bot that saw or shot at you within this many seconds, or sees you now, means you are in combat.");
            VerifyRoundTrip = Config.Bind("Step A", "Verify round trip", true,
                "After writing, read the save back and check it decodes into what was captured.");

            PitTeammateFate = Config.Bind("Pit FireTeam", "Teammates when a saved raid is abandoned or killed", TeammateFate.ShareYourFate,
                "Abandoning a saved raid (MIA) or a save made in combat (Killed): ShareYourFate reports your saved squadmates " +
                "lost, as Pit FireTeam does for fallen teammates; ComeHomeSafe leaves them as they were before the raid.");
            ResumeEnabled = Config.Bind("Step B", "Resume saved raids", true,
                "Starting a PMC raid on the map of your save resumes it instead of starting fresh.");

            new RaidStartPatch().Enable();
            new WeatherNodesPatch().Enable();
            new BossRunPatch().Enable();
            new OldWavesRunPatch().Enable();
            new LocalGameCreatePatch().Enable();

            new MainMenuPatch().Enable();
            new PauseMenuButtonPatch().Enable();
            new MenuPositionsPatch().Enable();
            new LocalGameMatchingPatch().Enable();
            new PitSquadPatch().Enable();
            new PrefabPathsPatch().Enable();
            new GameTimerStartPatch().Enable();
            new WaveSpawnGatePatch().Enable();
            new BossSpawnGatePatch().Enable();
            new FreeSpawnGatePatch().Enable();
            SainCompat.Enable();
            new SpawnLootPatch().Enable();
            new WorldSpawnLootPatch().Enable();
            new SpawnedInSessionPatch().Enable();
            new KeycardPatch().Enable();
            new GameStartPatch().Enable();
            ResolveBlackout.Enable();
            new BeginStatisticsSessionPatch().Enable();
            new TransitContinuePatch().Enable();
            new ManageGameQuestsPatch().Enable();
            new RaidEndPatch().Enable();
        }
    }
}
