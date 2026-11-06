using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SPSave.Game;
using UnityEngine;

namespace SPSave
{
    [BepInPlugin(Guid, "SPSave", Version)]
    [BepInDependency("com.SPT.core")]
    // Optional; loaded first so their types and patches are in place. See docs/B2-COMPAT.md.
    [BepInDependency(SainCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AbpsCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.chazut.orbit", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.joelhauser.spsave";
        public const string Version = "0.4.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> CaptureKey;
        internal static ConfigEntry<float> CombatWindowSeconds;
        internal static ConfigEntry<bool> VerifyRoundTrip;
        internal static ConfigEntry<bool> ResumeEnabled;
        internal static ConfigEntry<KeyboardShortcut> ReadyKey;

        /// <summary>&lt;game root&gt;\SPSave: saves\&lt;profile id&gt;\raid.spsave and captures.csv.</summary>
        internal static string SaveRoot => Path.Combine(BepInEx.Paths.GameRootPath, "SPSave");

        internal static string SavePath(string profileId) => Path.Combine(SaveRoot, "saves", profileId, "raid.spsave");

        private void Awake()
        {
            Log = Logger;
            CaptureKey = Config.Bind("Step A", "Capture key", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl),
                "Save & Quit: saves the raid, checks the save reads back, and closes the game. Refused in combat.");
            CombatWindowSeconds = Config.Bind("Step A", "Combat window (seconds)", 30f,
                "A bot that saw or shot at you within this many seconds, or sees you now, means you are in combat.");
            VerifyRoundTrip = Config.Bind("Step A", "Verify round trip", true,
                "After writing, read the save back and check it decodes into what was captured.");

            ResumeEnabled = Config.Bind("Step B", "Resume saved raids", true,
                "Starting a PMC raid on the map of your save resumes it instead of starting fresh.");
            ReadyKey = Config.Bind("Step B", "Ready key", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl),
                "A resumed raid stays paused until you press this.");

            new RaidStartPatch().Enable();
            new WeatherNodesPatch().Enable();
            new BossRunPatch().Enable();
            new OldWavesRunPatch().Enable();
            new LocalGameCreatePatch().Enable();

            new LocalGameMatchingPatch().Enable();
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
            new RaidEndPatch().Enable();
        }
    }
}
