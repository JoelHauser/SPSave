# B2 step 1: SAIN and ABPS against restored bots

Checked 2026-11-06 by decompiling the installed DLLs in `H:\SPT4.1.X` (ilspycmd): SAIN (client `SAIN.dll`, server
`SAINServerMod.dll`), SAIN Personality Mixer 1.0.0, ABPS 2.1.0 client and server, plus the game's `BossSpawnScenario`,
`BossLocationSpawn`, `NonWavesSpawnScenario`, `AbstractGame` and `TimerManager`. No code written yet.

## Verdict

Nothing blocks B2. SAIN takes a restored bot without help but rolls a new personality; ABPS has no say over a bot we
spawn ourselves, but its PMC waves are ordinary boss waves that restart from zero on resume unless we trim them. Both
fixes are ours to make; neither mod needs patching beyond one postfix on SAIN.

## SAIN

- **Attaches to any bot.** `AddBotComponentPatch` is a postfix on `BotOwner.PreActivate` → `BotSpawnController.AddBot`.
  Every bot made by `BotCreatorClient.ActivateBot` passes there. Bots are keyed by `ProfileId` (`BotDictionary`); a
  stale entry with the same id is removed first (`CheckExisting`), so reusing saved ids is safe.
- **Personality is re-rolled.** `SAINBotInfoClass` calls `PersonalityDictionary.GetPersonality` in its constructor and
  again in `ConfigureBot` (from `Init` and every preset change). Order: forced personality (preset), nickname match
  (`/sain/namepersonalities`, server `Data/NicknamePersonalities.json`), boss type, then random by chance; PMCs fall
  back to 33 % Chad. Only nickname and boss matches survive a resume, since the profile keeps both.
- **Personality Mixer** (`com.chazut.sainpersonalitymixer`) prefixes the same method at priority 800 for `pmcUSEC` and
  `pmcBEAR` only, cached per `SAINBotInfoClass` object (`ConditionalWeakTable`): a new bot object means a new draw.
  Its prefix ignores `__runOriginal`, so a prefix of ours returning false would be overwritten.
- **Fix:** save the personality at capture (`SAIN.Interop.SAINExternal.GetPersonality(BotOwner)` returns its name;
  reach it by reflection, SAIN stays optional) and add a **postfix** on `PersonalityDictionary.GetPersonality` that,
  for a restored profile id, sets `__result` and the `out` settings from the dictionary. A postfix runs after SAIN and
  the Mixer, so it wins in both paths. Missing personality in the current preset: leave SAIN's choice.
- Also re-rolled, accepted: `PercentageBeforeExtract` (when a bot heads for extract), search and hold-ground delays.
  They are "behaviour starts fresh", which Joel already accepted.
- `BotOwnerActivatePatch` skips vanilla `BotOwner.method_11` and disables bot metabolism. No effect on saved health.
- Squads: SAIN reads squads from the bot's `BotsGroup`. A saved group must come back as one `BotsGroup`.

## ABPS (acidphantasm-botplacementsystem)

Joel's settings: progressive and regressive boss chances **off**, despawning **off** (client cfg). That removes the two
ABPS features that would have counted a resumed raid twice.

**Server.** On load it replaces every map's `BossLocationSpawn` and `Waves` with its own: PMCs are boss waves
(`pmcUSEC`/`pmcBEAR`) with times from `delayBeforeFirstWave` plus `secondsBetweenWaves` (360 s here), starting PMCs
too. `/client/match/local/start` only records the map; `/client/match/local/end` regenerates that map's waves
(`RaidLifecycleService.RaidEnded` → `MapSpawns.RebuildMap`). Save & Quit sends no `local/end`, so the waves survive
until the server restarts, which re-randomizes everything. **The resume must restore the saved waves itself**, the
same way B1 restores loot: save `BossLocationSpawn[]` and `Waves` from the raid's location, put them back on resume.
`AdjustWavesPatch` (late-start scav raids) replaces `RaidTimeAdjustmentService.AdjustWaves`; not used for PMC raids.

**Client.**
- PMCs spawn through `PmcSpawnHookPatch`, a prefix on `BotBossSpawn.TrySpawn` for PMC waves: it picks spawn points
  away from players and from cached PMCs/scavs/bosses, then spawns through `_botCreator.ActivateBot`. Restored bots
  don't pass here, so ABPS can't move or replace them.
- Its caches (`Utility.CachedPmcs`, `CachedAssaultBots`, `CachedBosses`) feed the spawn-distance checks. Scavs and
  bosses enter through a postfix on `BotOwner.Create` (restored ones too). **PMCs enter only through ABPS's own group
  callback**, so restored PMCs are invisible to it: a new PMC wave could spawn on top of one. Fix: add restored PMC
  players to `Utility.CachedPmcs` (internal class, public static list, under `Utility.SpawnPointLock`) by reflection.
- `IsPlayerEnemyPatch` (prefix on `BotsGroup.IsPlayerEnemy`, PMC roles): same `BotsGroup` or same ABPS group ⇒ friends,
  otherwise enemies. Restoring each saved group as one `BotsGroup` keeps friends friendly.
- Caches and `BotsSpawnedPerPlayer` reset on `BotsController.Init`. That counter caps the scav trickle
  (`NonWaveSpawnBotsLimitPerPlayerPvE`); after a resume it restarts at 0, so a resumed raid can trickle in more scavs
  than an unbroken one. Optional fix: save it and set it back (public static double).
- `NonWavesSpawnSystemPatch` replaces `NonWavesSpawnScenario.Update`; it runs on `AbstractGame.PastTime` =
  `GameTimer.PastTimeSeconds()`, which B1 already shifts. The trickle continues on the right clock.
- `AssaultGroupPatch` turns every `assaultGroup` into `assault`. Nothing to do.

## ORBIT (com.chazut.orbit 3.1.0)

Checked 2026-11-06 (client `ORBIT.dll`, server `Orbit.Server.dll`). Bot behaviour: factions, looting, extraction,
squads, and Ghost Mode. Compatible as built; no patch of ours.

- **Ghost Mode** sleeps far-away bots: `bot.gameObject.SetActive(false)`, brain throttled, profile id in
  `DormancySystem.DormantProfileIds`. It doesn't unregister them, and `GameWorld.AllAlivePlayersList` only changes in
  `RegisterPlayer`/`UnregisterPlayer`, so **sleeping bots are captured like awake ones**. Ghost movement moves the
  real body (`NativeGhostSystem`: `GetPlayer.Transform.position = ...`), so the saved position is current. Ghost
  weapon visuals only hide the hands model. Ghost fights kill for real, so their dead are ordinary corpses.
- Restored bots come back **awake**; ORBIT puts far ones to sleep again by its own distance rules.
- **Registration**: prefix on `BotOwner.PreActivate` and the `BotOwner.BotState` setter, the same points a restored
  bot passes. Runtime starts at `BotsController.Init`. A new bot gets 8 s (game time) of spawn protection against AI
  contact; restored ones too. Harmless.
- **Squads** come from the bot's `BotsGroup` (`SquadRegistry.AddAgent`; scavs are always solo squads) and are sized
  `TargetMembersCount + 1`. Restoring the saved groups with their saved `TargetMembersCount` keeps ORBIT's squads.
- **Personality** is read from SAIN (`Orbit.Sain.SainPersonality`, by reflection), so pinning SAIN's personality pins
  ORBIT's. **Factions** go by `WildSpawnType`, which the saved profile keeps.
- Lost on resume, accepted (behaviour starts fresh): ORBIT's objectives, loot plans, ambushes, extraction plans.
- The server mod only serves its config UI (`/orbit/...` routes); it doesn't touch waves or raid start/end.
- ORBIT's tick is a postfix on `AICoreController.Update`; whether it runs during the freeze is unverified (harmless
  either way: at timeScale 0 ghost movement doesn't advance).

## The game's wave clock (what step 3 must handle)

- `BossLocationSpawn.Init()` rolls `ShallSpawn = IsTrue100(BossChance)` and clears `Activated`. It runs in
  `BossSpawnScenario.Create`/`Init` on every raid start, so a resume re-rolls every boss and PMC wave.
- `BossSpawnScenario.Run`: waves with `Time >= 0` get `TimerManager.MakeTimer(wave.Time)` (`Time.time`-based, so it
  stops while frozen at timeScale 0); `Time < 0` waves fire at once. The timers count from this session's `Run`, not
  from raid start: on resume, every wave would fire again from the beginning.
- **Fix:** save per wave `ShallSpawn` and `Activated` plus the raid time at save. On resume, drop waves that were
  activated or are due before the saved time (and start waves, `Time < 0`), shift the rest to `Time - played`, and
  restore `ShallSpawn` after `Init` (postfix on `BossSpawnScenario.Init`), so a boss that didn't roll still doesn't.

## Changes to the B2 plan

- Step 2 adds: save the SAIN personality per bot; the `GetPersonality` postfix; restored PMCs into ABPS's cache;
  one `BotsGroup` per saved group.
- Step 3 is now concrete: save and restore the location's waves, trim and shift them, keep `ShallSpawn`.
  Removing B1's `BotAmount = NoBots` must wait until step 3 lands, or a resume gets a full fresh set of waves.
- Unverified: whether restored bots that ABPS doesn't know as PMCs change its per-map PMC count (it counts by wave,
  not by alive PMCs, as far as read); Realism's effect on restored bot health.
