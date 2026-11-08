# Fika: what solo support would take

Read from source on 2026-11-07: Fika-Plugin `main` at `9572504` (Fika.Core 2.4.6) and Fika-Server-CSharp `main`
(`SptVersion ~4.1.3`, so it runs on SPT 4.1.6). Fika isn't installed in `H:\SPT4.1.X`; nothing here has been tried in
game. Scope: a solo raid hosted by the player's own client (`FikaBackendUtils.IsServer`, no peers). Joining someone
else's raid, headless hosts and real co-op stay out.

## How Fika runs a raid

- **Menu:** after the map screens, Fika turns `MatchMakerAcceptScreen` into its lobby (`MatchMakerUIScript`). Hosting
  calls `FikaBackendUtils.CreateMatch` (server `RaidCreate`, sets `ClientType = Host`, `GroupId`, `RaidCode`), then
  clicks the stock accept button.
- **`TarkovApplication.LocalGameMatching`:** Fika's prefix (`TarkovApplication_LocalGamePreparer_Patch`) starts its
  network manager and **lets the original run**, so the preload of `Session.Profile.GetAllPrefabPaths()` still happens.
- **`TarkovApplication.LocalGameCreate`:** replaced (`TarkovApplication_LocalGameCreator_Patch`, returns false). It
  reads `session.GetProfileBySide(Pmc)`, calls `LocalRaidStarted` (`local/start`), then
  `CoopGame.Create(...)` and `InitPlayer`. `LocalGame.Create` is never called.
- **`CoopGame : BaseLocalGame<EftGamePlayerOwner>`**. It keeps the base `SpawnLoot` and `RemoveUsedLocationKeycard`, but
  overrides:
  - `vmethod_3` (player creation): calls `profile.SetSpawnedInSession(false)` itself unless transit or Fika reconnect;
  - `vmethod_1` (bots): `HostGameController.StartBotSystemsAndCountdown`;
  - `vmethod_6`: `HostGameController.SetupEventsAndExfils` (starts `GameTimer`, resets session skill points, sets
    `Profile.Info.EntryPoint`, exfils);
  - `Stop`: its own end (`SavePlayer` → `LocalRaidEnded`).
- **`HostGameController`** builds its own `NonWavesSpawnScenario`, `WavesSpawnScenario` and `BossSpawnScenario` in its
  constructor (same classes and `LocalGame.smethod_8` as `LocalGame.Create`), a `BotCreatorClient` with Fika's
  `CreateBot`, and calls `BossSpawnScenario.Run` and `EventsController.SpawnAction` in `StartBotsSystem`. Weather
  goes through `WeatherController.SetWeatherNodes` (`GenerateWeathers`).
- **Server:** Fika replaces `LocationLifecycleService.StartLocalRaidAsync` (match loot) and prefixes
  `EndLocalRaidAsync` (runs the original for non-spectators). SPSave's server patches sit on `MatchCallbacks`
  and `ItemEventCallbacks`, which are called first, so the lock and unlock keep working.

## SPSave hook by hook

| SPSave hook | Under Fika | Work |
| - | - | - |
| `LocalGameMatchingPatch` (profile swap) | Runs (Fika doesn't skip the original) | None expected; check order with Fika's async prefix |
| `PrefabPathsPatch`, `SpawnLootPatch`, `WorldSpawnLootPatch`, `KeycardPatch`, `SpawnedInSessionPatch` | Base methods still called | None expected |
| `GameTimerStartPatch` | `SetupEventsAndExfils` calls `GameTimer.Start` | None expected |
| `LocalGameCreatePatch` (waves rebuilt after `LocalGame.Create`) | Never called | Postfix on the `HostGameController` constructor: replace `_bossSpawnScenario`, `_wavesSpawnScenario` |
| `BossRunPatch`, `OldWavesRunPatch`, `WeatherNodesPatch` | Same classes | None expected |
| `vmethod_6` patch (skill points, entry point, then `RaidRestore`) | Overridden, base not called | Same logic on `HostGameController.SetupEventsAndExfils` |
| `BotRestore` (`BotCreatorClient.ActivateBot`) | `BotsController` and `BotCreatorClient` are stock; the creator makes Fika bots | Check restored bots get Fika's net ids and AI |
| `FreshSpawnGate` (`BotsController` methods) | Same class | None expected |
| `RaidEndPatch` (`LocalGame.Stop`) | `CoopGame.Stop` | Same prefix on `CoopGame.Stop` |
| `ResolveController` / `ResolveBlackout` (Killed/MIA ending) | Ends through `CoopGame.Stop` → `SavePlayer` | Re-check the blackout's hand-off and the death path |
| `SavedRaidMenu` (RECONNECT → `method_27`) | Lands in Fika's lobby, which needs a host click | Call Fika's host path (`CreateMatch` + accept) by reflection, or ask the player to press Host |
| `PauseMenuButtonPatch` (SAVE & QUIT) | Fika also patches the pause menu (`DisconnectButton_Patch`) | Hide SAVE & QUIT unless host with no peers (`IsServer && IsSinglePlayer`) |
| Pit FireTeam | Unknown with Fika | Out of scope |

## Plan

1. Install Fika in a copy of the SPT folder (never in `H:\SPT4.1.X`).
2. `FikaCompat` (reflection only, like `PitFireTeamCompat`): detect `com.fika.core`; read `FikaBackendUtils.IsServer`,
   `IsSinglePlayer`; refuse saves otherwise.
3. Add the three Fika hooks above (waves, `SetupEventsAndExfils`, `CoopGame.Stop`); keep the stock ones.
4. Menu: start the saved raid through Fika's host path.
5. In game, on Factory: save, RECONNECT, LEAVE, combat save; then Customs.
