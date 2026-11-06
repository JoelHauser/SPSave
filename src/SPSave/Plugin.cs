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
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.joelhauser.spsave";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> CaptureKey;
        internal static ConfigEntry<float> CombatWindowSeconds;
        internal static ConfigEntry<bool> VerifyRoundTrip;

        /// <summary>&lt;game root&gt;\SPSave: saves\&lt;profile id&gt;\raid.spsave and captures.csv.</summary>
        internal static string SaveRoot => Path.Combine(BepInEx.Paths.GameRootPath, "SPSave");

        private void Awake()
        {
            Log = Logger;
            CaptureKey = Config.Bind("Step A", "Capture key", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl),
                "Captures the raid to disk and reads it back. Restores nothing.");
            CombatWindowSeconds = Config.Bind("Step A", "Combat window (seconds)", 30f,
                "A bot that saw or shot at you within this many seconds, or sees you now, means you are in combat.");
            VerifyRoundTrip = Config.Bind("Step A", "Verify round trip", true,
                "After writing, read the save back and check it decodes into what was captured.");

            new RaidStartPatch().Enable();
            new WeatherNodesPatch().Enable();
        }
    }
}
