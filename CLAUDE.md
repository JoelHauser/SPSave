# SPSave (Save & Resume Raid): working notes for Claude

SPT client mod (server mod to come): save a raid, quit, resume it later. Design and player promises:
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

**Now: B2, living bots.** Plan, riskiest first:
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
3. Let spawning continue: save the location's `BossLocationSpawn[]` and `Waves` (ABPS re-randomizes them on server
   restart), per-wave `ShallSpawn`/`Activated`; on resume drop fired and start waves, shift the rest by the time
   played, keep `ShallSpawn`. Then `FreshSpawnGate` goes.
4. Test on Factory with living scavs: same bots (profile ids), same places, same gear; then a second save and resume.
Bots come back unaware of the player (Joel's decision; combat saves are refused).

After B2: B3 (second save/resume of the same raid, extraction applies progress once, duplicate check), then C (server
mod: save storage, profile lock, Resume Raid button, Abandon = MIA, combat save = Killed), then D (pause-menu button,
version and mod-list checks, release).

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

- Save in combat resolves as Killed on next launch. Abandon is MIA. Both resolve without loading the raid.
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
  Anything that has to be recorded during play needs a reason here (currently: weather nodes, once per raid).
- Release zips hold only `BepInEx/plugins/SPSave.dll`. Version in the csproj and `Plugin.Version`.
