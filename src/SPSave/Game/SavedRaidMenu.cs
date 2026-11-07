using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using HarmonyLib;
using JsonType;
using SPSave.Core;
using SPT.Reflection.Patching;

namespace SPSave.Game
{
    /// <summary>
    /// While a save waits, the main menu is EFT's own "raid in progress" menu (docs/C3-PLAN.md), the one live EFT shows
    /// for a profile with status Busy. SPT never reaches it, but all of it is stock:
    /// - TarkovApplication.MainMenu(reconnectAction): with an action, MainMenuShowOperation.ShowMenuScreen shows
    ///   MainMenuReconnectScreenController (bottom bar disabled; Trade, Hideout, Character and the mode toggle hidden;
    ///   the warning panel shown; the play button is RECONNECT and calls the action);
    /// - the action shows ReconnectionScreen (map, level, character, banners) with RECONNECT and LEAVE.
    /// RECONNECT resumes; LEAVE asks in the stock dialog, then ends the raid as MIA (or Killed for a combat save, which
    /// shows only LEAVE). Both start the saved raid through the menu's own Ready handler, like pressing Ready.
    /// Joel: every piece of UI must look like stock EFT; the only text of ours is the Leave question.
    /// </summary>
    internal static class SavedRaidMenu
    {
        private const string AbandonText =
            "Abandon this raid? You will be listed as Missing in Action and lose the gear you took in.";
        private const string CombatNoticeText =
            "You quit this raid during combat, so you were killed. The gear you took in is lost.";
        private const string CombatLeaveText =
            "You quit this raid during combat. Leaving ends it: you will be listed as Killed and lose the gear you took in.";

        private static readonly FieldInfo MenuOperationField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

        /// <summary>
        /// The save waiting for this profile, after matching it with the server's lock; null if none. Discards a save
        /// with no matching lock (a wiped profile, or a save from before the lock existed) and drops a lock with no save.
        /// </summary>
        internal static Manifest WaitingSave(Profile profile)
        {
            if (profile == null || !Plugin.ResumeEnabled.Value)
            {
                return null;
            }
            var path = Plugin.SavePath(profile.Id);
            var sections = SnapshotFile.LoadNewestValid(path, out _, null);
            Manifest manifest = null;
            if (sections != null && sections.TryGetValue(RaidCapture.ManifestSection, out var bytes))
            {
                manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(System.Text.Encoding.UTF8.GetString(bytes));
            }
            if (manifest != null && manifest.ProfileId != profile.Id)
            {
                manifest = null;
            }

            var status = LockClient.Status();
            if (status == null)
            {
                // No server mod: the menu still locks, but nothing guards the server side.
                Plugin.Log.LogWarning("The SPSave server mod didn't answer; the profile isn't locked on the server.");
                return manifest;
            }
            if (manifest == null)
            {
                if (status.Locked)
                {
                    LockClient.Unlock();
                    Plugin.Log.LogInfo("Dropped the server's lock: no save is waiting for it.");
                }
                return null;
            }
            if (!status.Locked || status.ServerId != manifest.ServerId)
            {
                var retired = SnapshotFile.Retire(path, DateTime.UtcNow);
                if (status.Locked)
                {
                    LockClient.Unlock();
                }
                Plugin.Log.LogWarning($"Save for {manifest.Map} ({manifest.ServerId}) has no matching lock on the server " +
                                      $"(lock: {(status.Locked ? status.ServerId : "none")}); discarded to {retired}.");
                NotificationManager.DisplayWarningNotification("SPSave: your saved raid no longer matches your profile on the server, so it was discarded.");
                return null;
            }
            return manifest;
        }

        internal static void ShowReconnectScreen(TarkovApplication app, Manifest manifest)
        {
            var session = app.Session;
            var location = Location(session, manifest.Map);
            var combat = manifest.ResolveAs != null;
            // A save from another game build can't be loaded at all (ResumeSession refuses it), so it can't be resumed
            // or ended as MIA/Killed in game: LEAVE discards it, as a hard exit would.
            var gameVersion = RaidCapture.GameVersion();
            var otherBuild = manifest.GameVersion != gameVersion;
            if (otherBuild)
            {
                Plugin.Log.LogWarning($"Save was made on game {manifest.GameVersion}; this is {gameVersion}: it can only be discarded.");
            }
            var mods = ModListCheck.Compare(manifest.Plugins, CurrentPlugins(), new[] { Plugin.Guid });
            if (mods.NeedsConfirmation || mods.Added.Count > 0)
            {
                Plugin.Log.LogInfo($"Mods since the save: {mods.Missing.Count} missing, {mods.Changed.Count} changed, {mods.Added.Count} added. " +
                                   string.Join("; ", mods.Missing.Select(m => m + " missing").Concat(mods.Changed).Concat(mods.Added.Select(a => a + " added"))));
            }

            var controller = new ReconnectionScreen.ReconnectionScreenController(session.Profile, location, ESideType.Pmc,
                returnAllowed: false, nextScreenAllowed: !combat && !otherBuild, session);
            controller.OnReconnectAction += () =>
            {
                if (!mods.NeedsConfirmation)
                {
                    Start(app, manifest, null);
                    return;
                }
                ItemUiContext.Instance.ShowMessageWindow(string.Format(ModsChangedText, mods.Describe()),
                    () => Start(app, manifest, null),
                    () => Restore(controller),
                    "CONFIRMATION".Localized());
            };
            controller.OnLeave += () => ConfirmLeave(app, manifest, controller, combat, otherBuild);
            controller.ShowScreen(EScreenState.Queued);
            if (combat && !otherBuild)
            {
                // The reconnect screen has no text of its own to say why only LEAVE is there (map name, level, model
                // and banners only), so EFT's message window says it, OK only (no cancel action: no close button).
                ItemUiContext.Instance.ShowMessageWindow(CombatNoticeText, () => { }, null, "Attention".Localized());
                Plugin.Log.LogInfo("Combat save: told the player they quit in combat and were killed.");
            }
        }

        private const string ModsChangedText =
            "Your mods changed since this raid was saved: {0}. The raid may not come back as it was. Resume anyway?";
        private const string OtherBuildLeaveText =
            "This raid was saved on another version of the game and can't be loaded. Leaving discards it: your profile " +
            "stays as it was before the raid.";

        private static void ConfirmLeave(TarkovApplication app, Manifest manifest, ReconnectionScreen.ReconnectionScreenController controller, bool combat, bool otherBuild)
        {
            ItemUiContext.Instance.ShowMessageWindow(otherBuild ? OtherBuildLeaveText : combat ? CombatLeaveText : AbandonText,
                () =>
                {
                    if (otherBuild)
                    {
                        Discard(app, manifest, controller);
                    }
                    else
                    {
                        Start(app, manifest, ResolveOutcome.MissingInAction);
                    }
                },
                () => Restore(controller),
                "CONFIRMATION".Localized());
        }

        /// <summary>After a cancelled question: ReconnectionScreenController.Leave hid the task bar and turned the buttons off.</summary>
        private static void Restore(ReconnectionScreen.ReconnectionScreenController controller)
        {
            Plugin.Log.LogInfo("Question on the reconnect screen answered No: back to RECONNECT / LEAVE.");
            MonoBehaviourSingleton<PreloaderUI>.Instance.SetMenuTaskBarVisibility(visible: true);
            controller.Screen?.SetButtonsAvailability(available: true);
        }

        /// <summary>Scrap the raid as a hard exit does: the save goes, the lock goes, the profile is untouched; back to the normal menu.</summary>
        private static void Discard(TarkovApplication app, Manifest manifest, ReconnectionScreen.ReconnectionScreenController controller)
        {
            try
            {
                var retired = SnapshotFile.Retire(Plugin.SavePath(manifest.ProfileId), DateTime.UtcNow);
                LockClient.Unlock();
                Plugin.Log.LogInfo($"Save from another game build discarded to {retired}; profile unlocked.");
                controller.CloseScreen();
                app.MainMenu().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        Plugin.Log.LogError("Reopening the main menu failed: " + task.Exception);
                    }
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Discarding the save failed: " + e);
                Restore(controller);
            }
        }

        private static System.Collections.Generic.IEnumerable<string> CurrentPlugins()
        {
            return BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(info => info.Metadata.GUID + "@" + info.Metadata.Version);
        }

        /// <summary>
        /// The saved raid's map and side, then the menu's own Ready handler (MainMenuShowOperation.method_27: checks,
        /// settings, flushing the queue, OnReadyToStartMatching → LocalGameMatching, where the resume hooks in).
        /// Settings as SPT's LoadOfflineRaidScreenPatch fills them: waves and bots from the offline settings.
        /// </summary>
        private static void Start(TarkovApplication app, Manifest manifest, ResolveOutcome? outcome)
        {
            try
            {
                var menu = MenuOperationField?.GetValue(app) as MainMenuShowOperation
                           ?? throw new InvalidOperationException("The main menu isn't ready.");
                var raid = menu.raidSettings_0;
                var offline = menu.raidSettings_1;
                raid.SelectedLocation = Location(app.Session, manifest.Map)
                                        ?? throw new InvalidOperationException($"Map {manifest.Map} isn't in the server's location list.");
                raid.Side = ESideType.Pmc;
                raid.RaidMode = ERaidMode.Local;
                if (Enum.TryParse<EDateTime>(manifest.TimeVariant, out var time))
                {
                    raid.SelectedDateTime = time;
                }
                if (offline != null)
                {
                    // method_27 ends a local raid's settings with raidSettings_0.Apply(raidSettings_1), which copies
                    // the side, raid mode, bots, waves and the rest from these offline settings (0.7.0 test: their
                    // mode was still Online, so the game went to network matching). It sets the location and time
                    // on them itself.
                    offline.Side = ESideType.Pmc;
                    offline.RaidMode = ERaidMode.Local;
                    offline.SelectedLocation = raid.SelectedLocation;
                    offline.SelectedDateTime = raid.SelectedDateTime;
                    raid.WavesSettings = offline.WavesSettings;
                    raid.BotSettings = offline.BotSettings;
                }
                ResumeSession.Requested = outcome;
                Plugin.Log.LogInfo($"Starting the saved raid on {manifest.Map} ({(outcome == null ? "resume" : "leave: " + outcome)}): " +
                                   $"mode {raid.RaidMode}/{offline?.RaidMode}, side {raid.Side}, time {raid.SelectedDateTime}.");
                menu.method_27().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        ResumeSession.Requested = null;
                        Plugin.Log.LogError("Starting the saved raid failed: " + task.Exception);
                        NotificationManager.DisplayWarningNotification("SPSave: the saved raid couldn't be started. See the BepInEx log.");
                    }
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception e)
            {
                ResumeSession.Requested = null;
                Plugin.Log.LogError("Starting the saved raid failed: " + e);
                NotificationManager.DisplayWarningNotification("SPSave: the saved raid couldn't be started. See the BepInEx log.");
            }
        }

        private static LocationSettings.Location Location(IEftSession session, string map)
        {
            return session?.LocationSettings?.locations?.Values
                .FirstOrDefault(l => string.Equals(l.Id, map, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Opens the main menu in EFT's reconnect mode while a save waits (SavedRaidMenu).</summary>
    internal sealed class MainMenuPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.MainMenu));

        [PatchPrefix]
        private static void Prefix(TarkovApplication __instance, ref Action reconnectAction)
        {
            if (reconnectAction != null)
            {
                return;
            }
            try
            {
                var manifest = SavedRaidMenu.WaitingSave(__instance.Session?.Profile);
                if (manifest == null)
                {
                    return;
                }
                var app = __instance;
                reconnectAction = () => SavedRaidMenu.ShowReconnectScreen(app, manifest);
                Plugin.Log.LogInfo($"A saved raid on {manifest.Map} is waiting: main menu in reconnect mode{(manifest.ResolveAs != null ? " (saved in combat: Leave only)" : "")}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Checking for a saved raid at the main menu failed: " + e);
            }
        }
    }
}
