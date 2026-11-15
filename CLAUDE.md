# SPSave (Save & Resume Raid): working notes for Claude

SPT client mod plus a small server mod: save a PMC raid, quit, resume it later. Player promises: docs/DESIGN.md.
Joel's priorities: zero cost during normal play; verified APIs only (decompile with `ilspycmd`, never guess member
names); never call anything release-ready before his in-game checks.

## State (2026-11-14)

- **1.0.3 test build, uncommitted, not yet run in game:** `dist\SPSave-1.0.3-test1-SPT4.1.x.zip` (client and
  server 1.0.3). Two changes: the Actually Found In Raid fix (issue #1) and autosave. Update notes for players:
  docs/RELEASE-NOTES-1.0.3.md (the Forge page, docs/MOD-DESCRIPTION.md, still says 1.0.2; update it at release).
  Test steps: "Testing 1.0.3" below. Built on the work laptop, which has no `H:` and no SAIN: client against a
  mirror (`C:\HUH`'s Managed and BepInEx folders, Assembly-CSharp patched with SPT's delta through
  SharpHDiffPatch.Core, SAIN 4.5.1 from https://github.com/ArchangelWTF/SAIN, the up-to-date SAIN repo), server
  against `C:\HUH` (SPT 4.1.6, EFT 0.16.9.40743, same as `H:`). The mirror sits in a session scratchpad and may be
  gone; rebuild it or build on Joel's machine with the usual commands.

- **1.0.2 is public** (GitHub release `v1.0.2`, MIT; Forge upload is Joel's). GUID for client and server:
  `com.mybutthasarash.spsave`. The very first 1.0.0 build used `com.joelhauser.spsave` (`Plugin.OldGuid`, ignored by
  the mod-list check). 1.0.1: the 1.0.0 cleanup (two F12 settings, no `captures.csv`), Raid Review joined across
  saves, BossNotifier's list from the saved rolls, no corpse customization errors on a resume. 1.0.2: pause menu
  buttons placed in local space (a resolution change mid-session moved SAVE & QUIT and RESUME). `c-server` is an old
  copy of `main`; work on `main`.
- Installed in `H:\SPT4.1.X`: 1.0.2 client, 1.0.1 server (same code; the server was running at release).
- Passed in game: Factory, Customs and Woods (save, RECONNECT, second save of a resumed raid, LEAVE → MIA, combat
  save → Killed), SAIN/ABPS/ORBIT bots, Pit FireTeam squadmate, Menu Overhaul menus; Raid Review across a server
  restart (Customs, Factory), BossNotifier list from the save. Not seen yet: a thrown-away save finishing its Raid
  Review raid. How each step was reached: docs/HISTORY.md.

## Open work

- **Issue #1 (Forge, CrossWrap, 2026-11-14): "Save is always discarded"; fixed in 1.0.3, not seen in game.**
  Their log: every save failed `Save check` on two 7.62x39 BP rounds (magazine, chamber) found-in-raid True -> False.
  Cause: Actually Found In Raid (RuKira, `com.rukiragaming.actuallyfir`) patches `Ammo.SpawnedInSession` (in the
  game it always reads false, setter empty) to a `ConditionalWeakTable` keyed by the live `Ammo`; rebuilt items lose
  it (which step drops it is unproven; on paper both JSON write and `CreateItem` go through the patched property).
  Fix (`AmmoFoundInRaid`): capture lists found-in-raid ammo ids (player, bots, world) in `RaidState.FoundInRaidAmmo`;
  the read-back, the resume (player, bots, world loot) and Ready (live players) set them again through the setter;
  `InventoryCheck` counts an ammo found-in-raid difference as a warning (`AmmoFoundInRaidChanged`), not a failure.
  Reply to CrossWrap once 1.0.3 is out (Joel posts it).
- **Not yet seen in game:** Pit FireTeam recruits and boss allies; Interchange (power switch, Killa), Reserve (D2
  lever), Labs (keycard check at RECONNECT, no second keycard use), a big map (Streets, Lighthouse); the server's
  daily-quest freeze (`RepeatableQuestsFreezePatch`); zombies (`ZombieMidSessionPatch`; Joel won't test them).
- **Known gaps, accepted for now:** a mod removed while a save waits makes RECONNECT start a fresh raid (no
  duplication; better would be checking templates first and offering only LEAVE); the client mod removed while
  locked leaves the profile locked (fix: delete the lock file); a boss wave still in its `Delay` at save is lost;
  `Supports` escorts re-roll difficulty; small per-session stats (pedometer, session length) reset; the Halloween
  summoning state (`HalloweenEventController`) isn't saved. BTR, airdrops, artillery and transits don't come back.
- **Fika:** not supported. What solo support would take: docs/FIKA-COMPAT.md (Joel: leave it for now).
- **Autosave (players asked 2026-11-12; Joel said build it 2026-11-14; in 1.0.3, not seen in game).** F12 "Saving" →
  "Autosave every (minutes)", 0-60, default 0 (off). `CaptureController.AutoSaveLoop`: a coroutine that sleeps the
  whole interval (30 s idle checks while off), then `CaptureWriteLock(autosave: true)`, the same capture, write,
  `RoundTrip` and lock as SAVE & QUIT without quitting; `Manifest.AutoSave = true`. Skipped in combat (the last
  autosave stays), Scav raids, a dead player, and resume phases other than None/Live. The lock is set at every
  autosave (a crash gives no later chance). `Application.quitting` (Alt-F4, window close; not a crash) retires the
  autosave and unlocks unless SAVE & QUIT is quitting; a normal raid end already retires it (`RaidEndPatch`). A
  failed check or lock after an autosave existed leaves no save (`Retire` takes `.prev` too), so it unlocks.
  Accepted (Joel's call, my suggestion): a Task Manager kill looks like a crash; no `Player-prev.log` check. Costs
  to watch: the freeze per autosave (CrossWrap's Streets save took 1.5-2.3 s, round trip ~0.9 s of it); while
  locked mid-raid the server refuses item events and the rest (mods that sell or trade during a raid); the lock
  marks the running Raid Review raid exit 'Left', and at raid end the unlock (client `RaidEndPatch`, before
  `local/end`) runs Raid Review's thrown-away finish (timeInRaid from the lock) before END writes the real exit.
- **To report to pitAlex** (Pit FireTeam): it records menu button positions the first time it sees the MenuScreen
  and makes MY SQUAD once as a copy of CHARACTER, so a reconnect-mode first menu breaks Menu Overhaul's layout.

## Testing 1.0.3

Install the test zip into `H:\SPT4.1.X` with the game and server closed (both DLLs). Read `BepInEx\LogOutput.log`
before relaunching (the next launch overwrites it); a crashed session's Unity log is `Player-prev.log`.

**Issue #1 (Actually Found In Raid).** Install AFR 1.1.x (https://sp-mod.com/mod/2011/actually-found-in-raid-updated).
1. In a PMC raid, pick up loose ammo in raid (it shows found in raid with AFR). Load some into a magazine, chamber a
   round, keep some loose in a pocket.
2. SAVE & QUIT. Expect "raid saved" and the game closing. Log: `Save check: N found-in-raid rounds of ammo marked
   again`, no `Round trip failed`. A line `rounds of ammo changed found-in-raid (not a failure)` means putting the
   flags back didn't work, but the save was kept: send the log.
3. RECONNECT. Log: `Profile rebuilt from the save: N found-in-raid rounds ... marked again`, and at `Taking
   control` no "can't be marked again" warning. In game: the pocket ammo, the magazine's rounds and the chambered
   round (unload it) still show found in raid.
4. Kill a PMC (AFR makes its ammo found in raid), SAVE & QUIT near the body, RECONNECT: the body's ammo is still
   found in raid. Same for a living PMC carried over (kill it after the resume).
5. Without AFR (remove it): a save and resume work as in 1.0.2, and the log has no "marked again" lines.

**Autosave.** F12 → Saving → "Autosave every (minutes)" = 1 for testing.
1. Off by default: a fresh config shows 0, and nothing is autosaved (no `Autosaved` lines).
2. Set 1. In a quiet spot, wait a minute. Log: `Autosaved <map>: frame N ms ...` and `Profile locked on the server
   until this autosave ...`. Note the frame time on Factory and on Streets (the freeze players will feel).
3. Combat: get seen by a bot, stay in contact past the next tick. Log: `Autosave skipped: in combat. The last
   autosave stays.`
4. Crash: after an autosave, end the game process in Task Manager (stands in for a crash). Start the game: the
   menu is in reconnect mode, log `The waiting save is an autosave from ...`. RECONNECT: the raid comes back at the
   autosave's moment (position, gear, bots, time). After Ready, autosaves carry on in the resumed raid.
5. Crash, then LEAVE: the raid ends as MIA, as for a normal save. Raid Review (if installed) shows the raid.
6. Normal quit: after an autosave, Alt-F4 (and once with the window's X). Log: `Game closed normally during the
   raid: autosave discarded ... and profile unlocked`. Next launch: normal menu, no RECONNECT, stash usable.
7. Normal raid end: autosave, then extract (and once die). Log: `Raid ended normally (...), so its save can't be
   resumed`; the menu after is normal and the stash unlocked; the extract/death counts as usual.
8. SAVE & QUIT after an autosave: works as before, RECONNECT resumes the SAVE & QUIT moment, not the autosave.
9. While locked mid-raid: anything another mod does with the server during a raid (selling, traders) is refused;
   note what breaks. Raid Review: after an autosaved raid ends normally, its exit and duration are right.
10. Interval changes in F12 mid-raid take effect at the next tick; set back to 0 stops autosaving.

## Decisions (Joel)

- Autosave is opt-in (F12, default off). A Task Manager kill counts as a crash (accepted hole).

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

- **Actually Found In Raid** (`AmmoFoundInRaid`, not yet seen in game): its found-in-raid ammo flag lives outside the
  item, so saves list those ids and set them again after every rebuild; see issue #1 under Open work. SPSave never
  references the mod.
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
- **BossNotifier** (`BossNotifierCompat`, not yet seen in game): it lists bosses from `BossLocationSpawn.Init`, which
  first runs on the server's fresh roll that `LocalGame.Create` builds; after `Waves.Restore` its list is rebuilt from
  the saved rolls through its own postfix (2026-11-08: a Woods save without Goons announced Knight).
- **Raid Review** (`RaidReviewCompat` client, `RaidReviewContinuation` server; not yet seen in game): one raid across
  saves. The lock notes the profile's Raid Review raid and marks it exit `Left` (its cleanup deletes raids with no
  exit); the resume's `SessionManager.AddRaid` gets that id back, and the client moves Raid Review's stopwatch on by
  `RaidState.RaidReviewElapsedMs` right after its OnGameStarted postfix, so times follow on. At END the raid's
  `created_at`/`time` become the resume's (its purge keeps the last 30 by `created_at`). A save thrown away without a
  resume (`LockStore.Clear`, no resume START for that raid) gets `timeInRaid` from the lock's `raidReviewElapsedMs`.
  Seen working in game 2026-11-08 (Customs, server restart between the halves).

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
  https://github.com/JoelHauser/SPSave. The client build needs SAIN installed (compile-time reference); current SAIN
  releases: https://github.com/ArchangelWTF/SAIN (4.5.1 has the type names `SainCompat` uses).

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
