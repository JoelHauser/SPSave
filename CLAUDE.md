# SPSave (Save & Resume Raid): working notes for Claude

SPT client mod plus a small server mod: save a PMC raid, quit, resume it later. Player promises: docs/DESIGN.md.
Joel's priorities: zero cost during normal play; verified APIs only (decompile with `ilspycmd`, never guess member
names); never call anything release-ready before his in-game checks.

## State (2026-11-07)

- **1.0.0 is public** (GitHub release `v1.0.0`, MIT; Joel uploaded it to Forge). GUID for client and server:
  `com.mybutthasarash.spsave`. The very first 1.0.0 build used `com.joelhauser.spsave` (`Plugin.OldGuid`, ignored by
  the mod-list check).
- **`main` is past the release** (`f3a3d9e`, "Cleanup after 1.0.0"): F12 down to two settings ("Saving" combat
  window, "Pit FireTeam" teammates), no `captures.csv`, dead code and `tools/FrameProbe` removed. Ships as **1.0.1**
  after one in-game check (Save & Quit → RECONNECT, "Captured … round trip ok", F12 sections). `c-server` is an old
  copy of `main`; work on `main`.
- Installed in `H:\SPT4.1.X`: client from `main`; the server mod there is still the pre-GUID 1.0.0 build (the server
  was running when it was rebuilt): copy it in with the server stopped.
- Passed in game: Factory and Customs (save, RECONNECT, second save of a resumed raid, LEAVE → MIA, combat save →
  Killed), SAIN/ABPS/ORBIT bots, Pit FireTeam squadmate, Menu Overhaul menus. How each step was reached:
  docs/HISTORY.md.

## Open work

- **Not yet seen in game:** Pit FireTeam recruits and boss allies; Interchange (power switch, Killa), Reserve (D2
  lever), Labs (keycard check at RECONNECT, no second keycard use), a big map (Streets, Lighthouse); the server's
  daily-quest freeze (`RepeatableQuestsFreezePatch`); zombies (`ZombieMidSessionPatch`; Joel won't test them).
- **Known gaps, accepted for now:** a mod removed while a save waits makes RECONNECT start a fresh raid (no
  duplication; better would be checking templates first and offering only LEAVE); the client mod removed while
  locked leaves the profile locked (fix: delete the lock file); a boss wave still in its `Delay` at save is lost;
  `Supports` escorts re-roll difficulty; small per-session stats (pedometer, session length) reset; the Halloween
  summoning state (`HalloweenEventController`) isn't saved. BTR, airdrops, artillery and transits don't come back.
- **Fika:** not supported. What solo support would take: docs/FIKA-COMPAT.md (Joel: leave it for now).
- **To report to pitAlex** (Pit FireTeam): it records menu button positions the first time it sees the MenuScreen
  and makes MY SQUAD once as a copy of CHARACTER, so a reconnect-mode first menu breaks Menu Overhaul's layout.

## Decisions (Joel)

- A save in combat resolves as Killed on next launch; Abandon (LEAVE) is MIA. Both load the raid behind a black
  screen and end it through the game's own `GameEnd`, so XP, counters and fail-on-death quests apply as normal. The
  raid is never shown; the stock results screens and trip back to the menu stay.
- A hard exit (Alt-F4, crash) scraps the raid like standard SPT. A save is used up when the resume finishes loading.
- Restored bots start fresh: no awareness seeding.
- All UI looks like stock EFT: the game's own screens, buttons, dialogs and text. Our own words only where EFT has
  none (dialog sentences, the SAVE & QUIT label). A combat save's reconnect screen offers only LEAVE.
- Saves live at the SPT root (`<SPT>\SPSave\saves\<profile>\raid.spsave`); only the lock lives on the server.

## Rules

- Capture is read-only: a failed save leaves the raid exactly as it was.
- Nothing per frame in normal play. Anything recorded during play needs a reason here: the weather nodes and the
  `Time.time` each wave scenario ran at, once per raid each (the game keeps no copy).
- Every save is read back and checked (`RoundTrip`, `InventoryCheck`, `DuplicateCheck`); a save that fails is
  discarded and the raid carries on.
- Look up another mod's types in its own assembly; `AccessTools.TypeByName` scans every loaded type (a 955 ms save).

## How it works

- **Capture** (`CaptureController`, `RaidCapture`): `SAVE & QUIT` in the pause menu (`PauseMenuButtonPatch`, a clone
  of the stock LEAVE). Combat (`Core.CombatRule`) asks first and marks the manifest `ResolveAs = "Killed"`. Profiles
  and world loot are JSON (`ProfileCodec`, `WorldLoot`), bots, doors, clock, weather and waves in `state.json`; the
  file is written atomically (`Core.SnapshotFile`), read back, then the server lock is set before the game closes
  (no lock, no save). `SnapshotFile.Retire` moves used saves to `used\`, keeping the newest 3.
- **Lock** (`src/SPSave.Server`): `LockStore` (one file per profile in `user/mods/SPSave/locks`), routes
  `/spsave/lock|unlock|status`, Harmony prefixes on the core's callbacks (a mod router can't refuse a request): item
  events, scav save, prestige, scav regeneration, `local/start` except the lock's map and raid side (from the lock's
  `serverId`, "map.Pmc stamp"), Pit FireTeam's gear and roster actions, repeatable-quest rotation. Refusals answer
  `PlayerInRaid` (503002). Unlocked at `local/end` and on profile wipe or removal.
- **Menu** (`SavedRaidMenu`, `MainMenuPatch`): with a save waiting, `TarkovApplication.MainMenu` gets a
  `reconnectAction`, so the menu is EFT's reconnect mode and RECONNECT opens the stock `ReconnectionScreen`. A save
  without a matching lock is discarded; a lock without a save is dropped. RECONNECT and LEAVE both start the saved
  raid by filling `raidSettings_0` and `raidSettings_1` (Local, PMC, map, time) and calling
  `MainMenuShowOperation.method_27`. Another game build offers only LEAVE (discards); changed or missing mods ask
  first (`Core.ModListCheck`).
- **Resume** (`ResumeSession`, `ResumePatches`): `LocalGameMatchingPatch` swaps `Session.Profile` for the saved one
  before the game preloads its prefabs (later leaves gear unloaded); `PrefabPathsPatch` adds corpse and bot bundles.
  The saved world goes into `BaseLocalGame.SpawnLoot`, saved FIR flags back after `GameWorld.SpawnLoot`;
  `SetSpawnedInSession(false)` and `RemoveUsedLocationKeycard` are skipped; `GameTimer.Start` gets the time played.
  `LocalGame.Create` postfix rebuilds the boss scenario and old waves from the save (`Waves`, trimmed by
  `Core.WaveTrim`), aligned to the raid clock after loading. Fresh spawns are held (`FreshSpawnGate`) for saves
  without waves and during a Killed/MIA ending. `vmethod_6` keeps entry point and session skill points, then `RaidRestore` places the player
  and restores effects, doors, weather (private `backendWeatherCurve`, since Forecaster rewrites `SetWeatherNodes`)
  and time. `BotRestore` spawns each saved bot (`BotCreatorClient.ActivateBot`, saved `BotsGroup`s). Statistics and
  one-raid quest counters carry over (`StatisticsPatches`, `ManageGameQuestsPatch`). `ReadyController` uses up the
  save, unlocks and shifts the timer by the loading fade once PreloaderUI's black overlay goes.
- **Ending** (`ResolveController`, `ResolveBlackout`): the same load, no bots spawned, `ResolveBlackout` holds the
  overlay black (replacing `Co_ShowBlackImage`) until `Stop`'s callback; Killed = `ActiveHealthController.Kill`,
  MIA = `Stop(MissingInAction)`.
- **Raid end** (`RaidEndPatch` on `LocalGame.Stop`): a normal end of a raid that has a save discards the save. The
  server merges everything at `local/end` (it *assigns* level, skills, stats, quests, XP from the client profile and
  replaces equipment only; the stash is never touched, hence the lock), so progress counts once if the final client
  profile is cumulative.

## Other mods

Compatibility code is reflection only, with every member checked against the installed DLLs.

- **SAIN:** personalities pinned (`SainCompat`); SAIN takes on restored bots only after Ready. **ABPS:** PMC waves are
  boss waves; restored PMCs go into its cache (`AbpsCompat`). **ORBIT:** sleeping bots stay in
  `AllAlivePlayersList`, so they're captured. Details: docs/B2-COMPAT.md.
- **Pit FireTeam** (docs/PITFIRETEAM-COMPAT.md, `PitFireTeamCompat`): the squad is saved in `RaidState.PitFollowers`
  and respawned by Pit itself through its transit path (`PitSquadPatch`), then moved to saved spots. On Abandon or
  Killed, F12 decides: ShareYourFate reports squadmates lost, ComeHomeSafe doesn't. A missing member refuses saves
  with living followers.
- **Menu Overhaul + Pit FireTeam** (`MenuPositionsPatch`): after a reconnect-mode menu has shown, Pit's recorded
  button positions are cleared before each main menu Show, and MY SQUAD gets CHARACTER's `Background` (brush) state
  (`MatchSquadButtonBrush`), else its hover highlight sticks.
- **Halloween zombies** (off in SPT by default): `ZombieMidSessionPatch` marks the mid-raid spawn round done when the
  saved raid was past half time.

## Traps

- `GameWorld.SpawnLoot(initial: true)` marks every item found in raid and destroys containers missing from the loot
  list. Use `FullySearchedSearchController.Instance` for saved containers: others drop unsearched contents.
- Binary `ItemDescriptor`/`ProfileDescriptor` inventories store grids by index; a rig with an extra container shifts
  every grid's contents. Hence JSON (`FlatItem[]` by grid name) for profiles and world loot.
- The game's JSON `Vector3NullableSerializer` can't read back the null `PerfectPos` it writes (boss waves are copied
  field by field).
- `ProfileDescriptor` clones the pre-raid `Health`: set `Health = ActiveHealthController.Store()`. `Store()` keeps
  only `IRestorable` effects, so effect timers are captured from `ActiveHealthController.Effects` and rebuilt with
  `AddEffect<T>`. The in-raid health controller ignores `UpdateTime`, so time away doesn't heal.
- `ItemUiContext.ShowMessageWindow` in a raid accepts at once unless `forceShow: true`; in a raid `BattleUIScreen`
  disables `UIEventSystem`. Disabling the player to freeze it froze its animation.
- `method_27` ends with `raidSettings_0.Apply(raidSettings_1)`: set Local/PMC/map/time on both.
- EFT has its own `Paths`; use `BepInEx.Paths`. `Player` implements `IDissonancePlayer`: reference DissonanceVoip.dll.

## Workflow

- Install: `H:\SPT4.1.X` (SPT 4.1.6, EFT 0.16.9.40743; server folder `SPT_Runtime`). Repo
  https://github.com/JoelHauser/SPSave. The client build needs SAIN installed (compile-time reference).

  ```
  dotnet build src\SPSave -c Release -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true
  dotnet build src\SPSave.Server -c Release -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true
  dotnet run -c Release --project tests\SPSave.Tests
  dotnet run -c Release --project tools\SaveDump -- <raid.spsave> <out dir>
  ```

- A running SPT server locks `SPSave.Server.dll`: deploy it with the server stopped. The client copies while the
  game runs, but the game keeps the old one until restart.
- Logs: `BepInEx\LogOutput.log` and `Logs\log_<stamp>\` are overwritten by the next launch: read them before Joel
  relaunches. A crashed session's Unity log survives as `%USERPROFILE%\AppData\LocalLow\Battlestate Games\
  EscapeFromTarkov\Player-prev.log`. Server log: `SPT_Runtime\user\logs\spt\spt<date>.log`.
- Commit only when Joel asks. In PowerShell, pass commit messages with `git commit -F <file>`: a here-string with
  quotes failed silently once.
- Release: version in both csprojs, `Plugin.Version` and the server's `ModMetadata`; zip with .NET `ZipFile`
  (PowerShell 5.1 `Compress-Archive` writes backslash paths) holding `BepInEx/plugins/SPSave.dll` and
  `SPT_Runtime/user/mods/SPSave/SPSave.Server.dll`; `gh release create`. Forge page: docs/MOD-DESCRIPTION.md, plain
  Markdown (Forge prints Wiki.js tags like `{.is-warning}` as text).

## Layout

- `src/SPSave/Core`: save file format, combat rule, wave trim, duplicate and inventory checks, resolve rule, mod-list
  check. No Unity references; covered by `tests/SPSave.Tests` (plain .NET).
- `src/SPSave/Game`: everything that touches the game (capture, menu, resume, ending, other mods).
- `src/SPSave.Server`: the lock (net10.0).
- `tools/SaveDump`: validates a save and extracts its sections.
- docs: DESIGN.md (player promises), HISTORY.md (how each step was reached), TEST-PLAN.md (in-game checks per
  step), MOD-DESCRIPTION.md (Forge page), PITFIRETEAM-COMPAT.md, B2-COMPAT.md, FIKA-COMPAT.md, C-SERVER-API.md,
  C3-PLAN.md, STEP-A-RESULTS.md.
