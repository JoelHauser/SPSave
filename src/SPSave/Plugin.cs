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
        public const string Guid = "com.mybutthasarash.spsave";

        /// <summary>The GUID of the first 1.0.0 build; saves made with it list it among their plugins.</summary>
        public const string OldGuid = "com.joelhauser.spsave";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> CombatWindowSeconds;
        internal static ConfigEntry<TeammateFate> PitTeammateFate;

        /// <summary>&lt;game root&gt;\SPSave: saves\&lt;profile id&gt;\raid.spsave, with used-up saves under used\.</summary>
        internal static string SaveRoot => Path.Combine(BepInEx.Paths.GameRootPath, "SPSave");

        internal static string SavePath(string profileId) => Path.Combine(SaveRoot, "saves", profileId, "raid.spsave");

        private void Awake()
        {
            Log = Logger;
            CombatWindowSeconds = Config.Bind("Saving", "Combat window (seconds)", 30f,
                "A bot that saw or shot at you within this many seconds, or sees you now, means you are in combat: " +
                "saving then counts as dying.");
            PitTeammateFate = Config.Bind("Pit FireTeam", "Teammates when a saved raid is abandoned or killed", TeammateFate.ShareYourFate,
                "Abandoning a saved raid (MIA) or a save made in combat (Killed): ShareYourFate reports your saved squadmates " +
                "lost, as Pit FireTeam does for fallen teammates; ComeHomeSafe leaves them as they were before the raid.");

            // Recorded once per raid, for the save (weather nodes, wave scenario start times).
            new RaidStartPatch().Enable();
            new WeatherNodesPatch().Enable();
            new BossRunPatch().Enable();
            new OldWavesRunPatch().Enable();

            // Menus: SAVE & QUIT in the pause menu, the reconnect-mode main menu, menu fixes for other mods.
            new PauseMenuButtonPatch().Enable();
            new MainMenuPatch().Enable();
            new MenuPositionsPatch().Enable();

            // Resume: the saved profile, world, waves and bots go in while the raid loads.
            new LocalGameMatchingPatch().Enable();
            new PrefabPathsPatch().Enable();
            new LocalGameCreatePatch().Enable();
            new PitSquadPatch().Enable();
            new GameTimerStartPatch().Enable();
            new WaveSpawnGatePatch().Enable();
            new BossSpawnGatePatch().Enable();
            new FreeSpawnGatePatch().Enable();
            new SpawnLootPatch().Enable();
            new WorldSpawnLootPatch().Enable();
            new SpawnedInSessionPatch().Enable();
            new KeycardPatch().Enable();
            new GameStartPatch().Enable();
            new ZombieMidSessionPatch().Enable();
            SainCompat.Enable();

            // Progress carried over from earlier sessions of the raid.
            new BeginStatisticsSessionPatch().Enable();
            new TransitContinuePatch().Enable();
            new ManageGameQuestsPatch().Enable();

            // Raid end: a Killed/MIA ending behind a black screen, and a normal end discarding its save.
            ResolveBlackout.Enable();
            new RaidEndPatch().Enable();
        }
    }
}
