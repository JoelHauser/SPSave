using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using SPT.Common.Http;
using UnityEngine;

namespace SPSave.Game
{
    /// <summary>
    /// Pit FireTeam (xyz.pit.fireteam, optional) keeps persistent AI teammates. Checked against 1.1.1:
    /// docs/PITFIRETEAM-COMPAT.md. Everything here is reflection on its internal types; if any member is missing,
    /// <see cref="Ready"/> is false and a save with living followers is refused rather than resumed wrongly.
    ///
    /// Save: the player's followers (BossPlayers.GetFollowersByBoss) and Pit FireTeam's item bookkeeping for each.
    /// Resume: squadmates and boss allies come back through Pit FireTeam's own map-transit path
    /// (FollowerTransitStateCache, SpawnHelper.spawnMemberIds, the RaidTransit flag), so it spawns and registers them
    /// itself with their saved gear and health; BotRestore leaves them out. Recruits are restored by BotRestore and
    /// handed to BossPlayers.AddFollower, the call Pit FireTeam's own recruitment makes.
    /// </summary>
    internal static class PitFireTeamCompat
    {
        internal const string Guid = "xyz.pit.fireteam";
        private const string OutcomeRoute = "/singleplayer/pitfireteam/teammate/raid-outcomes";

        private static bool _looked;
        private static MethodInfo _getFollowersByBoss;
        private static MethodInfo _getBoss;
        private static MethodInfo _addFollower;
        private static MethodInfo _shallBeFollower;
        private static MethodInfo _getBot;
        private static MethodInfo _beginTeleportGrace;
        private static PropertyInfo _isSquadMate;
        private static Type _bossFollowerType;
        private static MethodInfo _getStoredEquipment;
        private static MethodInfo _getStoredItems;
        private static FieldInfo _spawnMemberIds;
        private static FieldInfo _spawnMemberIdsBoss;
        private static FieldInfo _spawnMemberIdsScav;
        private static MethodInfo _flagSet;
        private static FieldInfo _transitStates;
        private static ConstructorInfo _transitStateCtor;
        private static MethodInfo _transitClear;

        internal static bool Loaded => Chainloader.PluginInfos.ContainsKey(Guid);

        /// <summary>Once per raid, at raid start (CaptureController.Attach): the lookup's cost stays out of the save.</summary>
        internal static void Prepare() => Look();

        /// <summary>Pit FireTeam is loaded and every member this needs was found.</summary>
        internal static bool Ready
        {
            get
            {
                Look();
                return _transitStateCtor != null;
            }
        }

        private static void Look()
        {
            if (_looked)
            {
                return;
            }
            _looked = true;
            if (!Loaded)
            {
                return;
            }
            // Straight from Pit FireTeam's own assembly: AccessTools.TypeByName scans every loaded type, which took most
            // of a 1.3 s save with a large mod list (0.9.0's first save).
            var assembly = Chainloader.PluginInfos[Guid].Instance?.GetType().Assembly;
            Type Find(string name) => assembly?.GetType(name, throwOnError: false);
            var missing = new List<string>();
            T Need<T>(T member, string name) where T : class
            {
                if (member == null)
                {
                    missing.Add(name);
                }
                return member;
            }

            var bossPlayers = Need(Find("pitTeam.Modules.BossPlayers"), "BossPlayers");
            var follower = Need(Find("pitTeam.Components.BotFollowerPlayer"), "BotFollowerPlayer");
            var objects = Need(Find("pitTeam.Modules.InteractableObjects"), "InteractableObjects");
            var spawnHelper = Need(Find("pitTeam.Utils.SpawnHelper"), "SpawnHelper");
            var utils = Need(Find("pitTeam.Utils.Utils"), "Utils");
            var transit = Need(Find("pitTeam.Modules.FollowerTransitStateCache"), "FollowerTransitStateCache");
            _bossFollowerType = Need(Find("pitTeam.Components.BossFollowerPlayer"), "BossFollowerPlayer");

            var getFollowersByBoss = Need(bossPlayers != null ? AccessTools.Method(bossPlayers, "GetFollowersByBoss", new[] { typeof(string) }) : null, "BossPlayers.GetFollowersByBoss");
            var getBoss = Need(bossPlayers != null ? AccessTools.Method(bossPlayers, "GetBoss", new[] { typeof(string) }) : null, "BossPlayers.GetBoss");
            var addFollower = Need(bossPlayers != null ? AccessTools.Method(bossPlayers, "AddFollower") : null, "BossPlayers.AddFollower");
            var shallBeFollower = Need(bossPlayers != null ? AccessTools.Method(bossPlayers, "ShallBeFollower", new[] { typeof(string) }) : null, "BossPlayers.ShallBeFollower");
            var getBot = Need(follower != null ? AccessTools.Method(follower, "GetBot") : null, "BotFollowerPlayer.GetBot");
            var isSquadMate = Need(follower != null ? AccessTools.Property(follower, "IsSquadMate") : null, "BotFollowerPlayer.IsSquadMate");
            // Optional: keeps Pit FireTeam's follow logic from walking a placed teammate straight back (its own teleport).
            _beginTeleportGrace = follower != null ? AccessTools.Method(follower, "BeginTeleportGrace", new[] { typeof(Vector3) }) : null;
            var getStoredEquipment = Need(objects != null ? AccessTools.Method(objects, "GetStoredEquipment") : null, "InteractableObjects.GetStoredEquipment");
            var getStoredItems = Need(objects != null ? AccessTools.Method(objects, "GetStoredItems", new[] { typeof(string) }) : null, "InteractableObjects.GetStoredItems");
            var spawnMemberIds = Need(spawnHelper != null ? AccessTools.Field(spawnHelper, "spawnMemberIds") : null, "SpawnHelper.spawnMemberIds");
            var spawnMemberIdsBoss = Need(spawnHelper != null ? AccessTools.Field(spawnHelper, "spawnMemberIdsBoss") : null, "SpawnHelper.spawnMemberIdsBoss");
            var spawnMemberIdsScav = Need(spawnHelper != null ? AccessTools.Field(spawnHelper, "spawnMemberIdsScav") : null, "SpawnHelper.spawnMemberIdsScav");
            var flagSet = Need(utils != null ? AccessTools.Method(utils, "FlagSet", new[] { typeof(string), typeof(bool) }) : null, "Utils.FlagSet");
            var transitStates = Need(transit != null ? AccessTools.Field(transit, "StatesByKey") : null, "FollowerTransitStateCache.StatesByKey");
            var transitClear = Need(transit != null ? AccessTools.Method(transit, "Clear") : null, "FollowerTransitStateCache.Clear");
            var stateType = Need(transit != null ? AccessTools.Inner(transit, "TransitFollowerState") : null, "TransitFollowerState");
            var stateCtor = Need(stateType != null ? AccessTools.Constructor(stateType, new[] { typeof(Profile), typeof(List<string>), typeof(List<string>) }) : null, "TransitFollowerState(Profile, List, List)");

            var addParams = addFollower?.GetParameters();
            if (addParams != null && (addParams.Length < 4 || addParams[0].ParameterType != typeof(BotOwner) || addParams[2].ParameterType != typeof(bool) || addParams[3].ParameterType != typeof(WildSpawnType)))
            {
                missing.Add("BossPlayers.AddFollower(BotOwner, boss, bool, WildSpawnType, ...)");
            }

            var version = Chainloader.PluginInfos[Guid].Metadata.Version;
            if (missing.Count > 0)
            {
                Plugin.Log.LogWarning($"Pit FireTeam {version} is loaded but SPSave can't work with it (missing: {string.Join(", ", missing)}). " +
                                      "Saving is refused while you have living Pit FireTeam followers.");
                return;
            }
            _getFollowersByBoss = getFollowersByBoss;
            _getBoss = getBoss;
            _addFollower = addFollower;
            _shallBeFollower = shallBeFollower;
            _getBot = getBot;
            _isSquadMate = isSquadMate;
            _getStoredEquipment = getStoredEquipment;
            _getStoredItems = getStoredItems;
            _spawnMemberIds = spawnMemberIds;
            _spawnMemberIdsBoss = spawnMemberIdsBoss;
            _spawnMemberIdsScav = spawnMemberIdsScav;
            _flagSet = flagSet;
            _transitStates = transitStates;
            _transitClear = transitClear;
            _transitStateCtor = stateCtor;
            Plugin.Log.LogInfo($"Pit FireTeam {version} found: its squad is saved and resumed with the raid.");
        }

        /// <summary>
        /// The player's living followers, or null without Pit FireTeam. Throws (the save is refused, the raid goes on)
        /// when Pit FireTeam is loaded but not usable and the player might have followers.
        /// </summary>
        internal static List<PitFollowerState> Capture(Player player)
        {
            if (!Loaded)
            {
                return null;
            }
            if (!Ready)
            {
                throw new InvalidOperationException("Pit FireTeam is loaded but SPSave can't read its squad (see the warning at startup). " +
                                                    "Saving with Pit FireTeam followers is not supported with this version.");
            }
            var result = new List<PitFollowerState>();
            if (!(_getFollowersByBoss.Invoke(null, new object[] { player.ProfileId }) is IEnumerable followers))
            {
                return result;
            }
            var equipment = _getStoredEquipment.Invoke(null, null) as Dictionary<string, List<string>>;
            foreach (var follower in followers)
            {
                if (follower == null || !(_getBot.Invoke(follower, null) is BotOwner bot) || bot.IsDead || bot.GetPlayer == null ||
                    bot.GetPlayer.HealthController?.IsAlive != true)
                {
                    continue;
                }
                var kind = _bossFollowerType.IsInstanceOfType(follower) ? PitFollowerState.BossAlly
                    : (bool)_isSquadMate.GetValue(follower) ? PitFollowerState.Squadmate
                    : PitFollowerState.Recruit;
                var state = new PitFollowerState
                {
                    ProfileId = bot.ProfileId,
                    AccountId = bot.Profile?.AccountId,
                    Nickname = bot.Profile?.Nickname,
                    Kind = kind,
                    Role = bot.Profile?.Info?.Settings?.Role.ToString()
                };
                if (equipment != null && equipment.TryGetValue(bot.ProfileId, out var protectedIds) && protectedIds != null)
                {
                    state.ProtectedEquipmentIds.AddRange(protectedIds);
                }
                if (_getStoredItems.Invoke(null, new object[] { bot.ProfileId }) is List<string> tracked)
                {
                    state.TrackedReturnItemIds.AddRange(tracked);
                }
                result.Add(state);
            }
            return result;
        }

        internal static string Describe(List<PitFollowerState> followers)
        {
            if (followers == null)
            {
                return null;
            }
            if (followers.Count == 0)
            {
                return "Pit FireTeam: no followers";
            }
            return "Pit FireTeam: " + string.Join(", ", followers.GroupBy(f => f.Kind).Select(g => $"{g.Count()} {g.Key}"));
        }

        /// <summary>Saved followers that Pit FireTeam itself brings back (squadmates, boss allies): BotRestore skips them.</summary>
        internal static bool SpawnedByPit(RaidState state, string profileId)
        {
            return state?.PitFollowers != null && state.PitFollowers.Any(f => f.ProfileId == profileId && f.Kind != PitFollowerState.Recruit);
        }

        /// <summary>
        /// On a resume, after Pit FireTeam read the raid settings (it fills its spawn lists from the pre-raid group then):
        /// replace those lists with exactly the saved squad and put the saved profiles in its transit cache, as its
        /// TransitPointPatch does for a map transit. A raid being ended (Killed/MIA) gets no squad at all.
        /// </summary>
        internal static void PrepareResume(RaidState state, bool ending)
        {
            if (!Ready)
            {
                return;
            }
            try
            {
                var members = (List<string>)_spawnMemberIds.GetValue(null);
                var bosses = (List<WildSpawnType>)_spawnMemberIdsBoss.GetValue(null);
                var scavs = (List<string>)_spawnMemberIdsScav.GetValue(null);
                var before = members.Count + bosses.Count + scavs.Count;
                members.Clear();
                bosses.Clear();
                scavs.Clear();
                _transitClear.Invoke(null, null);

                var carried = new List<string>();
                var missing = new List<string>();
                if (!ending && state?.PitFollowers != null)
                {
                    var states = (IDictionary)_transitStates.GetValue(null);
                    foreach (var follower in state.PitFollowers.Where(f => f.Kind != PitFollowerState.Recruit))
                    {
                        if (!ResumeSession.BotProfiles.TryGetValue(follower.ProfileId, out var profile) ||
                            !Enum.TryParse(follower.Role, out WildSpawnType role))
                        {
                            missing.Add(follower.Nickname ?? follower.ProfileId);
                            continue;
                        }
                        var transitState = _transitStateCtor.Invoke(new object[]
                        {
                            profile, follower.ProtectedEquipmentIds.ToList(), follower.TrackedReturnItemIds.ToList()
                        });
                        // The keys TryCapture stores under.
                        foreach (var key in new[] { profile.AccountId, follower.AccountId, profile.ProfileId, follower.ProfileId, "role:" + role })
                        {
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                states[key] = transitState;
                            }
                        }
                        if (follower.Kind == PitFollowerState.BossAlly)
                        {
                            bosses.Add(role);
                        }
                        else
                        {
                            members.Add(follower.AccountId);
                        }
                        carried.Add(follower.Nickname ?? follower.ProfileId);
                    }
                }
                if (carried.Count > 0)
                {
                    _flagSet.Invoke(null, new object[] { "RaidTransit", true });
                }
                Plugin.Log.LogInfo($"Pit FireTeam squad for this {(ending ? "ending" : "resume")}: " +
                                   (carried.Count > 0 ? "carried back " + string.Join(", ", carried) : "none") +
                                   (missing.Count > 0 ? "; NOT carried (no saved profile): " + string.Join(", ", missing) : "") +
                                   (before > 0 ? $"; {before} from the pre-raid group not spawned" : "") + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Preparing the Pit FireTeam squad failed; teammates may not come back: " + e);
            }
        }

        /// <summary>
        /// Pit FireTeam spawns the carried squad in LocalGame.vmethod_1 (BotsEventsController.SpawnAction), next to where
        /// the player is then: the map's fresh spawn point, because the player is moved to the saved spot later, when
        /// the game starts (vmethod_6). Once the game has started, each carried teammate is moved to its own saved spot
        /// the way Pit FireTeam's teleport does it (BeginTeleportGrace, Mover.Stop, Player.Teleport).
        /// </summary>
        internal static void PlaceSquad(RaidState state)
        {
            if (!Ready || state?.PitFollowers == null || !state.PitFollowers.Any(f => f.Kind != PitFollowerState.Recruit))
            {
                return;
            }
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            if (world != null && world.GetComponent<SquadPlacer>() == null)
            {
                world.gameObject.AddComponent<SquadPlacer>().State = state;
            }
        }

        /// <summary>Waits (at most 30 s, only during a resume) for Pit FireTeam to register each carried teammate, then places it.</summary>
        private sealed class SquadPlacer : MonoBehaviour
        {
            internal RaidState State;

            private IEnumerator Start()
            {
                var waiting = State.PitFollowers.Where(f => f.Kind != PitFollowerState.Recruit).ToDictionary(f => f.ProfileId, f => f);
                var giveUpAt = Time.realtimeSinceStartup + 30f;
                while (waiting.Count > 0 && Time.realtimeSinceStartup < giveUpAt)
                {
                    yield return null;
                    var main = Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer;
                    if (main == null || !(_getFollowersByBoss.Invoke(null, new object[] { main.ProfileId }) is IEnumerable followers))
                    {
                        continue;
                    }
                    foreach (var follower in followers)
                    {
                        if (follower == null || !(_getBot.Invoke(follower, null) is BotOwner bot) || !waiting.TryGetValue(bot.ProfileId, out var saved) ||
                            bot.BotState != EBotState.Active || bot.GetPlayer == null)
                        {
                            continue;
                        }
                        waiting.Remove(bot.ProfileId);
                        Place(follower, bot, saved);
                    }
                }
                if (waiting.Count > 0)
                {
                    Plugin.Log.LogWarning("Pit FireTeam teammates not placed at their saved spots (never registered): " +
                                          string.Join(", ", waiting.Values.Select(f => f.Nickname)));
                }
                Destroy(this);
            }

            private void Place(object follower, BotOwner bot, PitFollowerState saved)
            {
                try
                {
                    var spot = State.Bots.FirstOrDefault(b => b.ProfileId == saved.ProfileId);
                    if (spot?.Position == null || spot.Position.Length != 3)
                    {
                        return;
                    }
                    var target = new Vector3(spot.Position[0], spot.Position[1], spot.Position[2]);
                    var was = bot.Position;
                    _beginTeleportGrace?.Invoke(follower, new object[] { target });
                    bot.Mover?.Stop();
                    bot.GetPlayer.Teleport(target);
                    if (spot.Rotation != null && spot.Rotation.Length == 2)
                    {
                        bot.GetPlayer.Rotation = new Vector2(spot.Rotation[0], spot.Rotation[1]);
                    }
                    Plugin.Log.LogInfo($"Pit FireTeam teammate {saved.Nickname} placed at its saved spot {target} ({Vector3.Distance(was, target):F0} m from where it spawned).");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Placing Pit FireTeam teammate {saved.Nickname} failed: {e.InnerException ?? e}");
                }
            }
        }

        internal const string MenuOverhaulGuid = "com.moxopixel.menuoverhaul";
        private static FieldInfo _menuPositions;
        private static Type _menuUiType;
        private static bool _menuLooked;
        private static bool _menuLogged;

        /// <summary>
        /// Pit FireTeam's MY SQUAD (SquadControlMenuUi.EnsureSquadButton) records the menu buttons' positions the first time
        /// it sees a MenuScreen and puts them back on every Show. Menu Overhaul lays the buttons out on Show, but not in the
        /// reconnect-mode menu SPSave opens while a save waits, so there Pit FireTeam recorded the stock positions and put
        /// them back over Menu Overhaul's layout after the raid (0.9.5 diagnostics: Overhaul's anchors, stock positions).
        /// With both mods loaded, the record is cleared before each main-menu Show outside raids and reconnect mode, so
        /// Pit FireTeam records Menu Overhaul's layout as on a fresh start. Without Menu Overhaul nothing is cleared: there
        /// the record is what keeps MY SQUAD's shift from adding up.
        /// </summary>
        internal static void ForgetMenuPositions(EFT.UI.MenuScreen screen)
        {
            // Only after EFT's reconnect-mode menu has shown in this session (the case the clear is for); a session
            // with no save leaves Pit FireTeam's record alone (0.9.8).
            if (!ReconnectMenuSeen || screen == null || !Loaded || !Chainloader.PluginInfos.ContainsKey(MenuOverhaulGuid))
            {
                return;
            }
            var record = MenuRecord(screen);
            if (record == null || record.Count == 0)
            {
                return;
            }
            record.Clear();
            if (!_menuLogged)
            {
                _menuLogged = true;
                Plugin.Log.LogInfo("Pit FireTeam's menu button record cleared before the main menu shows (Menu Overhaul lays it out again).");
            }
        }

        /// <summary>Set when the main menu opens in reconnect mode (MainMenuPatch); stays set for the session.</summary>
        internal static bool ReconnectMenuSeen;

        private const string SquadButtonName = "pitFireTeam_SquadControlButton";
        private static bool _brushLogged;

        /// <summary>
        /// Pit FireTeam makes MY SQUAD once, as a copy of CHARACTER, on the first menu it sees. Menu Overhaul hides the
        /// brush ("Background") on its five named buttons only, and its return-to-normal fades the brush in, not out.
        /// When the first menu is EFT's reconnect-mode menu (a save waiting), which Menu Overhaul doesn't restyle, the
        /// copy keeps CHARACTER's stock brush, and after the first hover MY SQUAD looks highlighted for good (Joel,
        /// 2026-11-07; 0.9.9's diagnostic: enter/exit and state changes were normal). Without a save waiting, the copy
        /// is made from the restyled CHARACTER, so this changes nothing there.
        /// </summary>
        internal static void MatchSquadButtonBrush(EFT.UI.MenuScreen screen)
        {
            if (screen == null || !Loaded || !Chainloader.PluginInfos.ContainsKey(MenuOverhaulGuid))
            {
                return;
            }
            var squadBrush = screen.transform.Find(SquadButtonName + "/Background");
            var characterBrush = screen.transform.Find("CharacterButton/Background");
            if (squadBrush == null || characterBrush == null || squadBrush.gameObject.activeSelf == characterBrush.gameObject.activeSelf)
            {
                return;
            }
            squadBrush.gameObject.SetActive(characterBrush.gameObject.activeSelf);
            if (!_brushLogged)
            {
                _brushLogged = true;
                Plugin.Log.LogInfo($"MY SQUAD's button brush set {(characterBrush.gameObject.activeSelf ? "on" : "off")} like CHARACTER's (Menu Overhaul).");
            }
        }

        private static IDictionary MenuRecord(EFT.UI.MenuScreen screen)
        {
            if (screen == null)
            {
                return null;
            }
            if (!_menuLooked)
            {
                _menuLooked = true;
                var assembly = Chainloader.PluginInfos[Guid].Instance?.GetType().Assembly;
                _menuUiType = assembly?.GetType("pitTeam.Components.SquadControlMenuUi", throwOnError: false);
                _menuPositions = _menuUiType != null ? AccessTools.Field(_menuUiType, "originalButtonPositions") : null;
                if (_menuPositions == null)
                {
                    Plugin.Log.LogWarning("Pit FireTeam's menu button record (SquadControlMenuUi.originalButtonPositions) not found; " +
                                          "the main menu may lose Menu Overhaul's layout after a resumed raid.");
                }
            }
            if (_menuPositions == null)
            {
                return null;
            }
            var ui = screen.GetComponent(_menuUiType);
            return ui != null ? _menuPositions.GetValue(ui) as IDictionary : null;
        }

        /// <summary>Before BotRestore spawns a saved recruit: Pit FireTeam treats it as a follower from activation on.</summary>
        internal static void MarkRecruit(RaidState state, string profileId)
        {
            if (!Ready || state?.PitFollowers == null || !state.PitFollowers.Any(f => f.ProfileId == profileId && f.Kind == PitFollowerState.Recruit))
            {
                return;
            }
            try
            {
                _shallBeFollower.Invoke(null, new object[] { profileId });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Marking recruit {profileId} for Pit FireTeam failed: {e.InnerException?.Message ?? e.Message}");
            }
        }

        /// <summary>After BotRestore activated a saved recruit: make it the player's follower again once it is active.</summary>
        internal static void RejoinRecruit(RaidState state, BotOwner bot)
        {
            if (!Ready || bot == null || state?.PitFollowers == null)
            {
                return;
            }
            var saved = state.PitFollowers.FirstOrDefault(f => f.ProfileId == bot.ProfileId && f.Kind == PitFollowerState.Recruit);
            if (saved == null)
            {
                return;
            }
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return;
            }
            var host = world.GetComponent<RecruitRejoin>() ?? world.gameObject.AddComponent<RecruitRejoin>();
            host.Add(bot, saved);
        }

        /// <summary>
        /// Abandon or Killed with "share your fate": every saved squadmate is reported lost through Pit FireTeam's own
        /// outcome route, as its raid end reports a fallen one (stats, health, gear rules on its server).
        /// </summary>
        internal static string ReportSquadLost(RaidState state, string raidId)
        {
            var squad = state?.PitFollowers?.Where(f => f.Kind == PitFollowerState.Squadmate && !string.IsNullOrEmpty(f.AccountId)).ToList();
            if (squad == null || squad.Count == 0)
            {
                return null;
            }
            if (!Loaded)
            {
                return $"Pit FireTeam is no longer installed: {squad.Count} squadmate(s) not reported (they keep their pre-raid state).";
            }
            try
            {
                var body = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InsuranceServerId = raidId,
                    Notify = true,
                    Entries = squad.Select(f => new { Aid = f.AccountId, ProfileId = f.ProfileId, Nickname = f.Nickname ?? "", Escaped = false }).ToList()
                });
                RequestHandler.PostJson(OutcomeRoute, body);
                return $"Pit FireTeam: {squad.Count} squadmate(s) reported lost ({string.Join(", ", squad.Select(f => f.Nickname))})";
            }
            catch (Exception e)
            {
                return "Pit FireTeam: reporting the squad lost FAILED: " + e.Message;
            }
        }

        /// <summary>Waits for each restored recruit to be active, then calls BossPlayers.AddFollower. Exists only while some wait.</summary>
        private sealed class RecruitRejoin : MonoBehaviour
        {
            private readonly List<KeyValuePair<BotOwner, PitFollowerState>> _waiting = new List<KeyValuePair<BotOwner, PitFollowerState>>();
            private float _giveUpAt;

            internal void Add(BotOwner bot, PitFollowerState saved)
            {
                _waiting.Add(new KeyValuePair<BotOwner, PitFollowerState>(bot, saved));
                _giveUpAt = Time.realtimeSinceStartup + 30f;
                if (_waiting.Count == 1)
                {
                    StartCoroutine(Run());
                }
            }

            private IEnumerator Run()
            {
                while (_waiting.Count > 0)
                {
                    yield return null;
                    var main = Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer;
                    var boss = main != null ? _getBoss.Invoke(null, new object[] { main.ProfileId }) : null;
                    for (var i = _waiting.Count - 1; i >= 0; i--)
                    {
                        var bot = _waiting[i].Key;
                        var saved = _waiting[i].Value;
                        if (bot == null || bot.IsDead)
                        {
                            _waiting.RemoveAt(i);
                            continue;
                        }
                        if (boss == null || bot.BotState != EBotState.Active)
                        {
                            continue;
                        }
                        _waiting.RemoveAt(i);
                        try
                        {
                            Enum.TryParse(saved.Role, out WildSpawnType role);
                            var args = _addFollower.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                            args[0] = bot;
                            args[1] = boss;
                            args[2] = false;
                            args[3] = role;
                            var added = _addFollower.Invoke(null, args);
                            Plugin.Log.LogInfo(added != null
                                ? $"Pit FireTeam recruit back as your follower: {saved.Nickname} {saved.ProfileId}."
                                : $"Pit FireTeam didn't take recruit {saved.Nickname} {saved.ProfileId} back as a follower.");
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogError($"Making recruit {saved.Nickname} {saved.ProfileId} a follower again failed: {e.InnerException ?? e}");
                        }
                    }
                    if (Time.realtimeSinceStartup > _giveUpAt && _waiting.Count > 0)
                    {
                        Plugin.Log.LogWarning("Pit FireTeam recruits not made followers again (never became active): " +
                                              string.Join(", ", _waiting.Select(w => w.Value.Nickname)));
                        _waiting.Clear();
                    }
                }
                Destroy(this);
            }
        }
    }
}
