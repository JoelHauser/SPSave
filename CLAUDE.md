# SPSave (Save & Resume Raid): working notes for Claude

SPT client mod plus a small server mod (the profile lock): save a raid, quit, resume it later. Design and player promises:
docs/DESIGN.md. Joel's priorities: zero cost during normal play; verified APIs only (no invented class names);
never call it release-ready before in-game checks.

## State

Step A (capture spike) done in game 2026-11-06 on Factory, Customs and Streets: results in docs/STEP-A-RESULTS.md.
Real save ~80–90 ms on Streets; no measurable normal-play cost (one off/on pair, not four: Joel won't do long runs).
Open: name the skipped loot entries (current build logs them).
B1 (resume player and world, no bots) **passed in game 2026-11-06** (Factory, full client + server restart): position,
loot, bodies, doors, magazines (106/106 items), searched bodies, on-screen timer, character and weapon. Ctrl+F10 is
Save & Quit; starting a PMC raid on the save's map resumes it, frozen until Ctrl+F11; the save is retired to
`used\<stamp>\` on Ready. Merged to main (`ba9aeb7`).

**B2, living bots: done 2026-11-06** (0.4.2, committed on branch `b2-bots`, not merged to main):
1. **Done 2026-11-06:** SAIN, ABPS and ORBIT checked against their code: docs/B2-COMPAT.md. Nothing blocks. SAIN
   attaches to any bot (`BotOwner.PreActivate`) but re-rolls personality; ABPS PMCs are boss waves that the server
   regenerates only on `local/end`, and the game restarts every wave timer from zero on resume. ORBIT's sleeping
   (Ghost Mode) bots stay in `AllAlivePlayersList`, so capture includes them; its squads follow `BotsGroup`.
2. **Passed in game 2026-11-06 (0.3.1, Factory):** 9/9 bots at exact positions incl. 3 PMCs, all 9 SAIN
   personalities kept (8 differed from SAIN's new draw), bleeds re-added on one, 5 restored scavs killed, no errors.
   0.3.0 had cleared the SAIN pins in Ready's `Reset()`; SAIN activates restored bots only after Ready, so
   `ResumeSession.GoLive` keeps them for the raid. `BotRestore` spawns each saved
   bot from its profile through `BotCreatorClient.ActivateBot(Profile, PositionNote, zone, ...)` and registers it with
   `BotSpawner.ActivateBotCallback`. Scavs regroup through the game's `GetGroupAndSetEnemies`; bosses, followers and
   PMCs get one `BotsGroup` per saved group with its saved `TargetMembersCount`, and a saved boss calls `SetBoss`.
   SAIN personality is pinned by a postfix (`SainCompat`), restored PMCs go into ABPS's cache (`AbpsCompat`), bot gear
   bundles join the preload, and bots keep their saved found-in-raid flags. B1's `NoBots` is gone (it also left
   `_canSpawn` false, so no `OnBeingHit`/`OnKill` for restored bots); `FreshSpawnGate` holds every fresh spawn in a
   resumed raid instead.
3. **0.4.0 in game 2026-11-06: boss waves didn't survive the save** (the game's JSON `Vector3NullableSerializer`
   can't read back the null `PerfectPos` it writes); bots and personalities fine. **0.4.1 passed in game**: saved at
   423 s, 4/7 waves restored, PMC waves fired at raid time 491/496 s against ~498/503 s originally: ~7 s early, the
   longer loading between the scenario's `Run` and the raid timer's start on a resume. **0.4.2 (not yet tested)**
   pushes the restored wave timers (`ITimer.Restart`) and wave `Time`s back by that gap when the raid timer starts
   (`Waves.AlignToRaidClock`, postfix on `GameTimer.Start`). `Waves`
   copies the boss scenario's `BossLocationSpawn[]` field by field into state.json (after `LocalGame.Create`'s
   in-place adjustments) with each wave's rolled `ShallSpawn`/`Activated`/`BornZone`/`EscortCount`, the old-style
   `SpawnWaves`, and `Time.time` since each scenario's `Run` (timers count from there, not from the raid timer).
   Bosses here spawn only at raid start (start waves, dropped on resume), so what carries on is ABPS's PMC waves
   (every 360 s from 500 s) and trigger waves. On resume, a postfix on `LocalGame.Create` rebuilds the boss scenario from
   the saved waves and replaces `SpawnWaves`, trimmed by `Core.WaveTrim` (fired and start waves dropped, the rest
   shifted, `ShallSpawn` kept). The server's waves are ignored (ABPS re-randomizes them on restart). ABPS's
   `BotsSpawnedPerPlayer` is restored. `FreshSpawnGate` now holds only when a save has no waves (before 0.4.0).
   Known gaps: a boss wave activated but still in its `Delay` at save is lost; `Init` re-rolls the difficulty of
   `Supports` escorts; Halloween zombie events aren't handled.
Bots come back unaware of the player (Joel's decision; combat saves are refused).

**B3, repeat and finish: done 2026-11-06** (0.5.0, Joel's call). Passed in game with one save, one resume and an extraction
(Factory): resume logged "1157 session XP so far, 3 kills, 3 on the kill list"; after extracting, the server profile
had Kills 8 (3 + 5), all 8 victims, XP counters from both sessions, the survived bonus and Sessions +1 once; the
save had 1,410 item ids and no duplicates. Not yet seen: a second save of a resumed raid; 0.4.2's wave alignment
firing (it ran, +5.1 s, but the raid ended before the next wave). Victim kill times count from the last resume.
- Server (decompiled `SPTarkov.Server.Core` `LocationLifecycleService`/`InRaidHelper`): `local/start` only resets
  server-side skill fatigue, generates loot and a new serverId. `local/end` *assigns* (doesn't add) level, skills,
  `Stats.Eft`, encyclopedia, `TaskConditionCounters`, achievements, quests, XP, trader standing and health from the
  client profile, and replaces equipment and quest items only (`SetInventory`; the stash is never touched, so the
  profile lock in C is required). So progress applies once if the final client profile is cumulative.
- Client resets at every raid start, now skipped on a resume: `HealthStatisticsManager/BaseStatisticsManager.
  BeginStatisticsSession` (from `Player.Init`) clears `SessionCounters` (all raid XP, kills), `DamageHistory` and
  `Victims` unless `TransitController.IsTransit`; `StatisticsPatches` answers IsTransit yes for the resumed profile
  during that call only. `Player.ManageGameQuests` (from `BaseLocalGame.SpawnLoot`) resets one-raid quest counters
  (`resetOnSessionEnd`) unless in transit; `ManageGameQuestsPatch` runs its other two steps without the reset. Quest
  loot comes from `location.Loot` (the saved world), so nothing spawns twice.
- Still lost per session (small stats, accepted for now): pedometer distance, `_stackableObjectsStartCount` session
  diffs, `OnlineAction` skill time (session length), healing/energy running totals.
- Duplicate check (`Core.DuplicateCheck`): every save checks that each item id is in one place (player, one bot,
  world); the capture line says "no duplicate items" or names them.

**Next: C, the server mod and the lock.** Started 2026-11-06 on branch `c-server` (from `b2-bots`). Without it a player can change their stash, gear, traders or
quests between Save & Quit and the resume, and the resume brings the saved kit back: duplication (raid end replaces
equipment only, never the stash) or lost gear, and quest progress overwritten by the save's. Scope (docs/DESIGN.md,
Joel's decisions above): save storage beside the profile; while a save exists, the profile is locked (traders,
flea, hideout transfers, mail, other raids) and the menu offers only Resume or Abandon; Abandon resolves as MIA and
a combat save as Killed, both by loading the raid and ending it (see the decision below); a hard exit scraps the
raid as standard SPT does, and a save used up on Ready is discarded and the profile unlocked on next launch. Plan,
riskiest first:
1. **Done 2026-11-06:** server mod API, lock points and the raid-end path, checked in decompiled code:
   docs/C-SERVER-API.md. A mod router can't refuse a request (every matching router runs, the core's first), so the
   lock is Harmony prefixes: `ItemEventCallbacks.HandleEvents` covers every item event; also `local/start` (other
   maps), `scavsave`, `prestige/obtain`, `savage/regenerate`. Open: how the client shows a refused item event;
   which of `SPTarkov.Server.Core.dll` / `.Patched.dll` the server loads.
2. The resolve path, **load then end** (Joel, 2026-11-06): behind the loading screen, run the normal resume, then
   call the game's own `GameEnd` with Killed or MIA before Ready. Reason: `Player.OnGameSessionEnd` (session XP into
   `Info.Experience`, death/MIA counters, quest exit and fail conditions through `ConditionalBook`) needs a live player
   and the save is captured before it; a raw server-side `EndLocalRaidAsync` from the save would skip all of it.
   **Built 2026-11-06 (0.6.0, not yet tested in game; docs/TEST-PLAN.md C2):** `Core.ResolveRule` picks the outcome
   (manifest `ResolveAs = "Killed"` from a combat save wins; else `ResumeSession.Requested`, which Abandon will set;
   for now a testing config "End the next resume as", used once). The resume loads as usual; `GameStartPatch`
   attaches `ResolveController` instead of `ReadyController` (no freeze). After the loading fade it uses up the save,
   sets `SessionStartTime` back by the time played, then Killed = `ActiveHealthController.Kill(Undefined)` (the
   game's `DiedEvent` → `InitiateGameStopping`, death panel, `Stop(Killed, 5 s)`) and MIA = `Stop(MissingInAction)`
   (what the raid timer calls). **0.6.0 MIA passed in game 2026-11-06** (save used up, server MIA counter 1, no
   errors) but the raid showed and the player could move before the fade (it waited for the loading fade, and
   `Stop`'s `StartBlackScreenShow` coroutine starts at alpha 0). Joel: a Killed/MIA ending must never show the raid.
   **0.6.1 Killed passed in game 2026-11-06** (Factory): save used up; server Killed 2→3, Sessions +1, win streak
   reset, +1,217 XP (995 session XP before the end multipliers); gear lost except armband, dogtag, pockets and the
   secure container (empty in the save too); no errors. `DeathCause` is a fresh one with no killer (Role `marksman`
   is `WildSpawnType` 0), i.e. the game's own `OnDeath` ran. Joel confirmed: screen stayed black, raid never shown,
   XP matched. **C2 done.**
   Storage (Joel, 2026-11-06): the save stays at the SPT root (`<SPT>\SPSave\saves\<profile>\raid.spsave`, as now);
   only the lock lives on the server.
   0.6.1: `ResolveBlackout` holds `PreloaderUI`'s overlay black from game start until `Stop`'s
   callback (GameEnd), by replacing `Co_ShowBlackImage` (every fade goes through it) while holding; the
   controller ends the raid one frame after start. Still to do: starting that load from the menu without picking the map (C3); combat
   saves still refused until the confirmation dialog exists.
3. Lock, menu (Resume / Abandon only) and storage. Joel: all UI must look like stock EFT (reuse the game's own
   buttons, text and dialogs; no custom styling). The Ready overlay's `OnGUI` label breaks that rule and needs
   replacing in D. **Built 2026-11-06 (0.7.0, not yet tested in game; docs/C3-PLAN.md, TEST-PLAN.md C3):**
   - Server mod `src/SPSave.Server` (net10.0): `LockStore` (one file per profile in `user/mods/SPSave/locks`),
     routes `/spsave/lock|unlock|status` (`LockRouter`), and `SPSaveLoader` (IOnLoad) enabling its own patches
     (`AbstractPatch.Enable` only works from the patch's assembly): refuse item events, scav save, prestige, scav
     regeneration; allow `local/start` only for the lock's map and side; clear the lock at `local/end` and on
     launcher wipe/remove. Refusals answer `PlayerInRaid` (503002).
   - Client: Save & Quit locks before closing (no lock confirmed = save discarded); Ready, the resolve and a normal
     raid end unlock. `MainMenuPatch` (prefix on `TarkovApplication.MainMenu`) matches the save with the server's
     lock (save without matching lock: discarded; lock without save: dropped) and passes a `reconnectAction`, so the
     menu is EFT's own reconnect mode and RECONNECT opens the stock `ReconnectionScreen`. RECONNECT resumes, LEAVE
     asks in `ItemUiContext.ShowMessageWindow` (caption `"CONFIRMATION".Localized()`), a combat save shows only
     LEAVE. Both start the saved raid by filling the menu's `raidSettings_0` (map, PMC, time variant, Local; waves
     and bots from `raidSettings_1`, as SPT's LoadOfflineRaidScreenPatch) and calling the menu's Ready handler
     `MainMenuShowOperation.method_27`. The testing setting from C2 is gone.
   - **0.7.0 in game 2026-11-06:** lock on Save & Quit, `/spsave/status` and the reconnect-mode menu worked;
     RECONNECT failed with "Network game matching failed": `method_27` ends with
     `raidSettings_0.Apply(raidSettings_1)`, which copies `RaidMode` (still Online), side, bots, waves etc. from the
     offline settings. **0.7.1** sets Local, PMC, map and time on `raidSettings_1` too. Save and lock were intact.
     0.7.1 got to the resume, then the server's own lock refused `local/start`: the lock held the character's side
     (`Usec`, from `manifest.Side`), the request the raid side (`Pmc`). The game hung on "starting local game".
     **0.7.2 / server 0.7.1:** the server takes the raid side from the lock's `serverId` ("map.Pmc stamp"), and the
     client sends "Pmc". **0.7.2 RECONNECT → resume passed in game 2026-11-06** (Factory, the 0.7.0 save and lock):
     reconnect-mode menu, stock reconnect screen, raid loaded without the map screens, 18/18 items, 9/9 bots and
     SAIN personalities back, save used up and profile unlocked on Ready (server logged it).
     **Abandon from the menu passed (0.7.2):** RECONNECT → LEAVE → Yes ended as MIA, save used up, unlocked, normal
     menu after. The server log shows that save was a second save of a resumed raid (unlock 23:55:41 on Ready, lock
     again 23:55:44) and it rebuilt 6/6 items. EFT's errors.log had 18 `SAIN BotComponent.OnDisable` null refs at
     unload: 9 restored bots SAIN hadn't taken on (it does so after Ready). **0.7.3:** an ending spawns no restored
     bots, preloads none of their gear and holds fresh spawns.
     **0.7.3 passed in game 2026-11-07 (Factory):** LEAVE → No returned to the reconnect screen with working buttons
     (Joel confirmed; the cancel wasn't logged then, it is from 0.8.0), then RECONNECT resumed cleanly: lock at save,
     6/6 items, 8/8 bots, unlock on Ready on both sides, no errors in SPSave's, EFT's or the server's logs, no SAIN
     errors. **C3 done** (on Factory only; other maps: see Upcoming steps).
   - Combat saves at capture: done in D (pause-menu button with a stock confirmation).
   - Known gap: if the resume fails to load the save after RECONNECT, the raid starts fresh on the saved map; its
     raid end then unlocks and the next menu discards the save (no duplication: the stash was locked throughout).
**D, pause menu and resume polish: done and passed in game by 2026-11-07** (client 0.9.7). What it is now:
- `PauseMenuButtonPatch` (postfix on `MenuScreen.ChangeScreenInGameStatus`): SAVE & QUIT is a clone of the stock
  `_disconnectButton` (LEAVE) with our label. The parent doesn't stack its children, so `Targets` places one evenly
  spaced column in world space: LEAVE, SAVE & QUIT, RESUME (`_hideScreenButton`), and Pit FireTeam's SQUAD SETTINGS
  when present (step halfway between the stock and Pit's). `LayoutKeeper` rechecks a frame and 0.3 s after opening
  (Pit FireTeam restores recorded positions on every Show) and logs "something moved the buttons" when it corrects.
  Hidden in Scav raids, while finishing a resume and while ending one. LEAVE is hidden during the loading fade.
- Combat save: stock confirmation, then manifest `ResolveAs = "Killed"`; combat is judged again after the answer. The
  combat save's reconnect screen shows EFT's message window (caption `"Attention".Localized()`, OK only) and only LEAVE.
- No Ready window, no freeze, no hotkeys (Joel): `ReadyController` waits for PreloaderUI's black overlay to go, then
  uses up the save, unlocks and shifts the raid timer and panels by the fade time. Nothing runs per frame in play.
- `SnapshotFile.Retire` prunes `used\` to the newest 3 stamped folders; a second retire in one second gets `-2`.
- Reconnect screen: another game build shows only LEAVE (discards the save, untested path). Missing or changed mods
  (`Core.ModListCheck`, SPSave itself ignored) ask before RECONNECT; added mods are only logged.
- Traps learned: `ItemUiContext.ShowMessageWindow` in a raid accepts at once unless `forceShow: true`. In a raid
  `BattleUIScreen` disables `UIEventSystem` (a cursor alone can't click). Disabling the player (old `Freeze`) froze
  its animation: arms stuck, a second without control. `AccessTools.TypeByName` scans every loaded type (a 955 ms
  save with this mod list): look types up in the other mod's own assembly.

**Pit FireTeam compatibility (Joel: required): passed in game 2026-11-07** (Pit FireTeam 1.1.1 + Menu Overhaul 1.3.0,
Factory). Design and verified members: docs/PITFIRETEAM-COMPAT.md. `Game/PitFireTeamCompat.cs`, reflection only;
every member checked against the installed DLLs; a missing member refuses saves with living followers.
- Save: `RaidState.PitFollowers` (squadmate / bossAlly / recruit, ids, Pit's protected and tracked item ids).
- Resume: `PitSquadPatch` (postfix on `SendRaidSettings`, `Priority.Last`, after Pit's own) replaces Pit's spawn lists
  with the saved squad and fills its transit cache (`FollowerTransitStateCache.StatesByKey`), sets `RaidTransit`;
  Pit FireTeam spawns them itself; `BotRestore` skips them. Pit spawns in `LocalGame.vmethod_1` next to the player's
  *fresh* spawn, before `vmethod_6` moves the player, so `SquadPlacer` moves each to its saved spot once registered
  (Pit's own `BeginTeleportGrace`, `Mover.Stop`, `Player.Teleport`). Recruits: `ShallBeFollower`, then `AddFollower`.
- Abandon/Killed: no squad spawned; F12 "Teammates when a saved raid is abandoned or killed": `ShareYourFate` posts
  each squadmate lost to `/singleplayer/pitfireteam/teammate/raid-outcomes`; `ComeHomeSafe` posts nothing.
- Server 0.8.0 `PitFireTeamLock`: refuses 9 gear/roster methods on `FriendlyTeammateCallbacks` while locked.
- Menu clash fixed (`Game/MenuPositionsPatch.cs`): Menu Overhaul doesn't lay out the reconnect-mode menu, and Pit
  FireTeam records button positions the first time it sees the MenuScreen and restores them on every Show, so after
  a resumed raid it put stock positions over Menu Overhaul's layout. The record is cleared before each
  `MainMenuScreenController` Show when Menu Overhaul is loaded. (`_reconnectAvailable` is set after Show: unusable.)
- Not yet seen in game: recruits rejoining, boss allies, Abandon/Killed with a squadmate. Report the menu-record
  behaviour to pitAlex (it keeps whatever layout it sees first).

Repo: **1.0.0 released 2026-11-07** (GitHub release v1.0.0, MIT): `c-server` merged into `main`, tagged `v1.0.0`; `c-server`
and `main` are the same. Release zip: `dist/SPSave-1.0.0.zip` (dist/ is ignored by git).

## Upcoming steps (as of 2026-11-07, in order; 1.0.0 installed)

1. **Customs: passed in game 2026-11-07** (two runs, each save → RECONNECT → second save → RECONNECT → LEAVE):
   night time variant and weather (550/552 nodes) came back as left (Joel), an opened door stayed open, the Pit
   FireTeam squadmate came back at its exact spot, bodies looked normal; second saves of resumed raids round-tripped
   cleanly (1,599 and 2,030 world items); Abandon ended as MIA, unlocked on both sides, logged "1 squadmate(s) reported
   lost" and My Squad showed the teammate lost. EFT's errors.log on the MIA ending: 5 NREs in
   `Corpse.InitBody` → `LoadCustomizationResources` (a corpse customization part with no bundle gives a null
   `ResourceKey`; the game catches it and the body still builds), plus `PerfectCulling` NREs at unload.
   Still open: a Pit FireTeam recruit (none recruited yet); Reshala not met.
   **MY SQUAD stuck hover highlight: fixed in 0.9.10, passed in game 2026-11-07** (Joel: "worked perfectly").
   Pit FireTeam makes MY SQUAD once, as a copy of CHARACTER, on the first menu of the session; with a save waiting
   that's the reconnect-mode menu, which Menu Overhaul doesn't restyle, so the copy kept the stock brush
   (`Background` child). Menu Overhaul hides `Background` on its 5 named buttons only, and its animated idle fades the
   image to alpha 1, so the brush stayed after a hover. `PitFireTeamCompat.MatchSquadButtonBrush` (postfix,
   `Priority.Last`, on `MenuScreen.Show`, and a frame later) gives MY SQUAD CHARACTER's brush state. Found with a
   temporary `[hover]` diagnostic (0.9.9–0.9.10, removed in 0.9.11): enter/exit and state changes were normal. Since
   0.9.8 Pit's menu record is cleared only once the reconnect-mode menu has shown (`ReconnectMenuSeen`).
   Crash seen once (0.9.10, not SPSave): native `Mesh.Internal_Create` on a thread-pool thread. Menu Overhaul
   1.3.0's `PlayerProfileViewService.ApplyMainMenuProfileViewAsync` awaits `AddPlayerModel().ConfigureAwait(false)`,
   then sets text colours off the main thread; Name Plate's `MenuOverhaulCompat` postfix calls TMP `ForceMeshUpdate`
   there. Timing-dependent; didn't recur. Unity's log of a crashed session survives as LocalLow `Player-prev.log`.
   Logs: read them before the next launch, which overwrites BepInEx's and EFT's.
2. **Other maps** as Joel has time: Interchange (power switch, Killa), Reserve (D2 lever), Labs (keycard check at
   RECONNECT, no second keycard use), a big map load (Streets or Lighthouse).
3. **Known gaps** (Joel 2026-11-07: work on SPT version, daily quests, zombies; ignore the rest for now):
   - SPT version: already covered. The manifest has `SptVersion` (client `com.SPT.core`, "4.1.6") and the plugin list
     has `com.SPT.core@…`, so an SPT update shows in `ModListCheck`'s "Your mods changed… Resume anyway?".
   - Daily/weekly quests: the menu calls `client/repeatalbeQuests/activityPeriods` on every load (not locked);
     `RepeatableQuestController.GetClientRepeatableQuests` rotates an expired set (`ProcessExpiredQuests` removes the
     old statuses from `pmcData.Quests`, new set generated), and `local/end` then assigns the saved `Quests`
     (`pmcData.Quests = profile.Quests`; `RepeatableQuests` isn't assigned). **Server 0.9.0 (installed, not
     tested in game):** `RepeatableQuestsFreezePatch` sets every set's `EndTime` to max while locked and restores it in the
     postfix, so nothing rotates until the first menu after the raid. Test: Save & Quit, stop the server, set a
     daily `EndTime` in the past in the profile, start it, menu → log "Kept N expired repeatable quest set(s)";
     resume and extract → next menu rotates.
   - Zombies (Halloween infection): off here (`seasonalevents.json` `zombieSettings.enabled: false`; Halloween
     itself on 10/28–11/9, summoning enabled). `BotsEventsController` builds `BotHalloweenWithZombies` from
     `Halloween2024` location config, `Activate` subscribes every bot in `BotsList` and `OnBotAdd`, so restored zombies
     should be picked up. `CheckMidSessionTriggerBotsSpawn` fires `SpawnAction` once at half the session time
     (`_midSessionSpawned`, not saved): a save after the half point would fire it again on resume. Pursuits and crowd
     spawns are runtime state (lost, like awareness). **0.9.12 (installed, not tested in game; Joel won't test
     zombies):** `ZombieMidSessionPatch` (postfix on `BotHalloweenWithZombies.Activate`, resume only) sets
     `_midSessionSpawned` from the saved clock (past ≥ half session). Unchecked: zombie asset pools for restored
     zombies, any player infection effect, the summoning event's state (`HalloweenEventController`: ritual, exits).
     Mod page: list zombies as untested.
   - Not now: removed mod's items in a save; client mod removed while locked.
   Older B notes: a boss wave in its `Delay` at save is lost, `Supports` escorts re-roll
   difficulty, Halloween zombies, small per-session stats. BTR, airdrops, artillery, transits, seasonal events don't
   come back (DESIGN.md).
4. **Merge** `c-server` into main when Joel says; commit and push each later step when he asks.
5. **Release: done 1.0.0 (Joel, 2026-11-07).** For later releases: bump the version in both csprojs, `Plugin.Version` and the
   server `ModMetadata`; README and mod page (`docs/MOD-DESCRIPTION.md`, rewritten 2026-11-07 as the 1.0.0 page);
   release zip per Rules (build it with .NET ZipFile: PS 5.1 Compress-Archive writes backslash paths); GitHub
   release. Never call a release ready before Joel's checks with his usual mods.

## How the resume hooks in (B1)

`LocalGameMatchingPatch` (prefix on `TarkovApplication.LocalGameMatching`, which preloads `Session.Profile.GetAllPrefabPaths()` before `LocalGameCreate`; swapping later left saved gear unloaded: no weapon, broken model) loads the save and sets
`Session.Profile = new Profile(saved descriptor)` (the setter is on `ClientBackendSession`; after a raid the client
reloads the profile with `Session.GetProfiles`, carrying only `EftStats` across). Then: `GameTimer.Start` gets
startTime = now − played; fresh bot spawns are held (`FreshSpawnGate`, three `BotsController` methods);
`BaseLocalGame.SpawnLoot` gets the saved `location.Loot`; `GameWorld.SpawnLoot` postfix puts saved FIR flags back;
`Profile.SetSpawnedInSession(false)` and `RemoveUsedLocationKeycard` are skipped; `vmethod_6` keeps `_entryPoint` and
skill fatigue, then `RaidRestore` places the player and sets effects, doors (`SetInitialSyncState`), weather (private
`backendWeatherCurve`, bypassing `SetWeatherNodes` because Forecaster rewrites its nodes) and time
(`GameDateTime.ResetForce`). `PrefabPathsPatch` adds corpse head/clothing bundles to that preload (else "usec_head_5.bundle is not loaded", no bodies). `ReadyController` (no freeze since 0.8.6) waits for PreloaderUI's black overlay to fade, then uses up the save and
shifts the raid timer and `MainTimerPanel._dateTime` by the fade time (Pause mod's method). "Ready" in the history
above means that moment.
The in-raid `ActiveHealthController` ignores `UpdateTime`, so time away doesn't regenerate health. Corpses load their
own customization (`Corpse.Init` → `InitBody`).

Target install: `H:\SPT4.1.X` (SPT 4.1.6, EFT 0.16.9.40743). Repo https://github.com/JoelHauser/SPSave.git.

## Decisions (Joel, 2026-11-06)

- Save in combat resolves as Killed on next launch. Abandon is MIA. Both resolve by loading the raid behind the
  loading screen and ending it through the game's own `GameEnd` (changed from "without loading" on 2026-11-06, so
  XP, counters and fail-on-death quests apply exactly as normal).
- Hard exit (Alt-F4, crash) scraps the raid like standard SPT. A save is used up when the resume finishes loading.
- No combat continuity: restored bots start fresh. Don't add awareness seeding.
- All UI must look like stock EFT: reuse the game's own screens, buttons, dialogs and text; no custom styling. Our
  own words only where EFT has none (dialog sentences, the SAVE & QUIT label).
- Saves stay at the SPT root (`<SPT>\SPSave\saves\<profile>\raid.spsave`); only the lock lives on the server.
- A combat save's reconnect screen shows only LEAVE (ends as Killed). Abandon's question: "Abandon this raid? You will
  be listed as Missing in Action and lose the gear you took in."
- A Killed/MIA ending never shows the raid: the screen stays black from load to results.
- Killed/MIA keeps the stock results screens and the trip back to the menu, like a real death (Joel, 2026-11-06,
  asked about skipping them or the load; chose to keep both).

## Layout

- `src/SPSave/Core`: save file format and atomic write, combat rule, capture report, wave trim, duplicate check,
  resolve rule, mod-list check. No Unity or game references.
- `src/SPSave/Game`: capture (`RaidCapture`, `CaptureController`, `PauseMenuButton`), resume (`ResumeSession`,
  `ResumePatches`, `RaidRestore`, `BotRestore`, `Waves`, `ReadyController`), ending as Killed/MIA
  (`ResolveController`, `ResolveBlackout`), the locked menu (`SavedRaidMenu`), server calls (`LockClient`), SAIN/ABPS
  compatibility, Pit FireTeam (`PitFireTeamCompat`, `MenuPositionsPatch` for its clash with Menu Overhaul).
- `tests/SPSave.Tests`: plain .NET, `dotnet run -c Release`.
- `tools/FrameProbe`: separate measurement plugin for the on/off comparison. Not shipped.
- `tools/SaveDump`: validates a save and extracts its sections.
- `src/SPSave.Server`: the server mod (net10.0): the profile lock while a save waits (`PitFireTeamLock` for Pit
  FireTeam's teammate actions).
- docs: DESIGN.md (player promises), TEST-PLAN.md (in-game checks per step, latest first), PITFIRETEAM-COMPAT.md,
  MOD-DESCRIPTION.md (draft mod page), C-SERVER-API.md, C3-PLAN.md, B2-COMPAT.md, STEP-A-RESULTS.md.

## Build

The install must have SAIN (compile-time reference for the personality pin; at run time SAIN is optional).

```
dotnet build src\SPSave -c Release -p:SPTPath="H:\SPT4.1.X" [-p:DeployToSPT=true]
dotnet build src\SPSave.Server -c Release -p:SPTPath="H:\SPT4.1.X" [-p:DeployToSPT=true]
dotnet run -c Release --project tests\SPSave.Tests
```

Decompile with `ilspycmd` (global tool) when a new API is needed; never guess member names.

## Verified facts (decompiled 4.1.6 / 0.16.9.40743)

- Raid end is only `BaseLocalGame.GameEnd` → `IEftSession.LocalRaidEnded` (`/client/match/local/end`). The server
  merges everything from the client profile there and writes nothing during a raid. `wipeOnRaidStart` is false here.
- Raid start: `TarkovApplication.LocalGameCreate` reads `Session.Profile`, calls `local/start` (server generates loot;
  request has `ShouldSkipLootGeneration`), then `LocalGame.Create(..., sessionTime, ...)` and `Run`.
- Profiles: `new ProfileDescriptor(profile, searchController)`, `new Profile(ProfileDescriptor)`, binary
  `WriteEFTProfileDescriptor`/`ReadEFTProfileDescriptor` (EFT.BinarySerialization, Mirror NetworkWriter/Reader).
  The descriptor clones `profile.Health`, which is pre-raid: set `Health = ActiveHealthController.Store()`.
- `Store()` keeps only `IRestorable` effects and drops most timers. Capture timings from `ActiveHealthController.Effects`
  and rebuild with `AddEffect<T>(part, delay, work, residue, strength)`.
- World loot is saved as JSON through `LootItemSerializer` (`JsonLootItem[]`, flat items named by `slotId`, corpses
  come back as `JsonCorpse`): see `WorldLoot`. **Not** the binary `ItemDescriptor` path: it stores grids by index
  (`GridDescriptor.GridNumber`), and a live rig on a corpse had two containers its template lacks, so a magazine
  landed in the wrong grid and `Grid.AddItemWithoutRestrictions` parked it outside (lost; "doesn't fit to grid" in
  EFT's errors.log).
- Profiles are saved as JSON too (`ProfileCodec`): binary `WriteEFTProfileDescriptor` carries the inventory as an
  index-based `ItemDescriptor` tree that `ToInventory` prefers; Joel's rig had one extra container, so every grid's
  contents shifted down one on rebuild. JSON `InventoryDescriptor` flattens the live `_inventory` to `FlatItem[]`
  (`OnJSONSerializing`), the form SPT sends at raid end. Mod items can name grids oddly ("GridView (5)" on a
  Pack 'n' Strap-style armband); by-name rebuild handles that.
- Save & Quit rebuilds the profile from the written file and compares every item on the player (`InventoryCheck`);
  any loss refuses the save.
- (Old notes) World: `ItemBinarySerializer.SerializeLootData`/`DeserializeLootData`, `Write/ReadEFTLootDataDescriptor`,
  `JsonCorpse` with `Bones` from `Corpse.GetTransformSync()` (`TransformSyncs` is cached). `GameWorld.SpawnLoot` loads it;
  containers match by `IStaticLoot.Id`. Use `FullySearchedSearchController.Instance`: other controllers drop the contents of
  unsearched containers.
- Doors: `WorldInteractiveObject.DoorState`, `SetDoorState(state, force)`.
- Clock: `GameTimer.Start(startTime, sessionTime)`; `GameDateTime.Reset(...)`; the weather curve runs on game time
  (`GameDateTime.Calculate`), so saved `WeatherNode[]` plus game time restores weather. `WeatherController` keeps no
  node array, hence `WeatherNodesPatch`.
- Bots: `BotCreatorClient.ActivateBot(Profile, PositionNote, zone, ...)`; awareness from
  `BotOwner.EnemiesController.EnemyInfos[player]` (`IsVisible`, `PersonalSeenTime`, `PersonalLastSeenTime`,
  `PersonalLastShootTime`).
- `Player` implements `IDissonancePlayer`: reference DissonanceVoip.dll. EFT has its own `Paths`; use `BepInEx.Paths`.

## Traps for Step B (resume)

- `BaseLocalGame.vmethod_3` calls `profile.SetSpawnedInSession(false)`: wipes found-in-raid on everything looted earlier.
- `GameWorld.SpawnLoot(..., initial: true)` sets found-in-raid on every spawned item, including the player's own dropped
  gear. Keep saved flags. `initial: true` also destroys containers missing from the loot list.
- `_playerFactory` uses up a Labs keycard again (`RemoveUsedLocationKeycard`).
- `vmethod_6` resets skill points earned this session and overwrites `Profile.Info.EntryPoint` (exfil eligibility).
- `LocalGame.vmethod_5` replaces weather with the backend's after `InitWeather`.
- Opening waves and boss rolls run in `LocalGame.vmethod_1`. ABPS (installed) spawns PMCs on its own.
- Unverified: SAIN, ABPS and Realism with restored bots and health; one-raid quest counters at raid start; the server's
  bot-details cache after a restart; BTR services calling the server.

## Rules

- Capture is read-only. A failed save must leave the raid exactly as it was.
- Nothing per frame in normal play (the Step A hotkey check went in 0.8.5).
  Anything that has to be recorded during play needs a reason here (currently: weather nodes, once per raid; the
  `Time.time` each wave scenario ran at, once per raid, because wave timers count from it and the game keeps no copy).
- Release zips hold `BepInEx/plugins/SPSave.dll` and `SPT_Runtime/user/mods/SPSave/SPSave.Server.dll` (this install's
  server folder is `SPT_Runtime`). Versions in both csprojs, `Plugin.Version` and the server's `ModMetadata`.
