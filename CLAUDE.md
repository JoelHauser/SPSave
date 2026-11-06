# SPSave (Save & Resume Raid): working notes for Claude

SPT client mod (server mod next, step C): save a raid, quit, resume it later. Design and player promises:
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
   replacing in D.
Then D (pause-menu button, Ready to Resume prompt, version and mod-list checks, release).

## How the resume hooks in (B1)

`LocalGameMatchingPatch` (prefix on `TarkovApplication.LocalGameMatching`, which preloads `Session.Profile.GetAllPrefabPaths()` before `LocalGameCreate`; swapping later left saved gear unloaded: no weapon, broken model) loads the save and sets
`Session.Profile = new Profile(saved descriptor)` (the setter is on `ClientBackendSession`; after a raid the client
reloads the profile with `Session.GetProfiles`, carrying only `EftStats` across). Then: `GameTimer.Start` gets
startTime = now − played; fresh bot spawns are held (`FreshSpawnGate`, three `BotsController` methods);
`BaseLocalGame.SpawnLoot` gets the saved `location.Loot`; `GameWorld.SpawnLoot` postfix puts saved FIR flags back;
`Profile.SetSpawnedInSession(false)` and `RemoveUsedLocationKeycard` are skipped; `vmethod_6` keeps `_entryPoint` and
skill fatigue, then `RaidRestore` places the player and sets effects, doors (`SetInitialSyncState`), weather (private
`backendWeatherCurve`, bypassing `SetWeatherNodes` because Forecaster rewrites its nodes) and time
(`GameDateTime.ResetForce`). `PrefabPathsPatch` adds corpse head/clothing bundles to that preload (else "usec_head_5.bundle is not loaded", no bodies). `ReadyController` freezes through `Freeze` (Pause mod's method, patches installed only while frozen: timeScale 0 alone let player updates run with dt 0 and broke the camera and collisions), stops time only after PreloaderUI's black overlay fades (it steps by `Time.deltaTime`), and, on Ready, shifts the timer and
`MainTimerPanel._dateTime` by the frozen time (Pause mod's method).
The in-raid `ActiveHealthController` ignores `UpdateTime`, so time away doesn't regenerate health. Corpses load their
own customization (`Corpse.Init` → `InitBody`).

Target install: `H:\SPT4.1.X` (SPT 4.1.6, EFT 0.16.9.40743). Repo https://github.com/JoelHauser/SPSave.git.

## Decisions (Joel, 2026-11-06)

- Save in combat resolves as Killed on next launch. Abandon is MIA. Both resolve by loading the raid behind the
  loading screen and ending it through the game's own `GameEnd` (changed from "without loading" on 2026-11-06, so
  XP, counters and fail-on-death quests apply exactly as normal).
- Hard exit (Alt-F4, crash) scraps the raid like standard SPT. A save is used up on Ready to Resume.
- No combat continuity: restored bots start fresh. Don't add awareness seeding.

## Layout

- `src/SPSave/Core`: save file format and atomic write, combat rule, capture report. No Unity or game references.
- `src/SPSave/Game`: capture (`RaidCapture`), read-back check (`RoundTrip`), hotkey controller, two patches.
- `tests/SPSave.Tests`: plain .NET, `dotnet run -c Release`.
- `tools/FrameProbe`: separate measurement plugin for the on/off comparison. Not shipped.
- `tools/SaveDump`: validates a save and extracts its sections.

## Build

The install must have SAIN (compile-time reference for the personality pin; at run time SAIN is optional).

```
dotnet build src\SPSave -c Release -p:SPTPath="H:\SPT4.1.X" [-p:DeployToSPT=true]
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
- Nothing per frame in normal play. Step A's hotkey check is the only exception and goes away with the menu button.
  Anything that has to be recorded during play needs a reason here (currently: weather nodes, once per raid; the
  `Time.time` each wave scenario ran at, once per raid, because wave timers count from it and the game keeps no copy).
- Release zips hold only `BepInEx/plugins/SPSave.dll`. Version in the csproj and `Plugin.Version`.
