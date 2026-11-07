# Pit FireTeam compatibility: API check

Checked 2026-11-06 against Pit FireTeam **1.1.1** (tag `1.1.1`, commit `7105ec5`, https://github.com/pitAlex/SPT-PitFireTeam),
the version installed in `H:\SPT4.1.X` (`BepInEx\plugins\pitFireTeam\pitFireTeam.dll` 1.1.1,
`SPT_Runtime\user\mods\pitFireTeam-ServerMod`). Game code checked in the decompiled 0.16.9.40743 `Assembly-CSharp`.
Nothing here has run in game yet.

## What Pit FireTeam is

Persistent AI teammates. The server mod stores each saved teammate (profile, equipment, often bought or moved from the
player's stash, insurance, health between raids, XP, skills) in its own LiteDB database. The client spawns them at raid
start, gives them follower AI (BigBrain layers) and settles their outcome when the raid ends. There are also *recruits*:
friendly bots picked up during a raid (Allegiance mode, friendly Scavs), raid-only, not saved squad members.

## Verified facts (1.1.1 source)

**Spawn.**
- `RaidStartPatch`: postfix on `EftClientBackendSession.SendRaidSettings` fills `SpawnHelper.spawnMemberIds` with the
  account ids of the teammates in the pre-raid group (`MainMenuControllerPatch.GroupPlayers`).
- The game calls `SendRaidSettings` from `TarkovApplication.LocalGameCreate` for every local raid **except a transit**
  (`if (!_raidSettings.isInTransition)`). SPSave's RECONNECT goes through `LocalGameCreate`, so this runs on a resume.
- `BotsEventsControllerSpawnPatch` (postfix on `BotsEventsController.SpawnAction`) calls `SpawnFollowers()`, which calls
  `BotsControllerPatch.SpawnGroupBots(playerBoss)` for those ids. It creates the bots through the raid's `BotCreator`
  directly (not `BotsController.ActivateBots*`), next to the player, so **SPSave's `FreshSpawnGate` does not hold it**.
- Saved-teammate settings come from the server (`/client/game/bot/followerdetails`, `LoadSavedSpawnSettings`); fresh
  kits are prepared on the pre-raid accept screen (`CompanionAcceptScreenPreparation`, `/client/game/bot/followergenerate`).

**Transit (the closest existing analogue of a resume).**
- `TransitPointPatch` (postfix on `TransitPoint.SeparateGroupPlayers`): for each living squadmate,
  `FollowerTransitStateCache.TryCapture(bot, protectedEquipmentIds, trackedReturnItemIds, out profile)` snapshots the
  live profile (gear, health), adds the account id to `spawnMemberIds` and sets `Utils.FlagSet("RaidTransit", true)`.
- In the next raid `SpawnGroupBots` takes the carried profile via `FollowerTransitStateCache.TryConsumeProfile(aid, role,
  out profile)` instead of the server's, and `InteractableObjects` re-registers the carried protected/tracked item ids
  (`TryConsumeProtectedEquipmentIds`, `TryConsumeTrackedReturnItemIds`).
- `RaidTransit` also makes `CompanionRaidPreparation` skip kit preparation and makes karma and kill reports continue
  the raid (`PmcKarmaRuntime.BeginRaid(true)`, `SquadRaidKillReport.BeginRaid(true)`).
- The cache is a set of **internal static, in-memory** dictionaries: empty after a game restart.

**Runtime identity.** `BossPlayers.GetFollowersByBoss(playerProfileId)` → `BotFollowerPlayer` (`IsSquadMate` for saved
teammates, `GetBot()` → `BotOwner`). All `internal`: reachable by reflection only.

**Raid end.**
- Client: patches on `LocalGame.Stop`, `LocalGame.CleanUp`, `BotsController.StopGettingInfo`; outcomes go to
  `/singleplayer/pitfireteam/teammate/raid-outcomes`, returned loot to `/singleplayer/returnitems` (mail).
- Server: its own handlers on `/client/match/local/start` and `/client/match/local/end` (insurance diagnostics, Scav
  cargo, kill messages, `RemoveProtectedTeammateItemsFromExtractedProfile`).
- `FriendlyPostRaidService.ProtectedRaidItemIds` (items handed to teammates, kept out of the player's extracted profile)
  and `FriendlyModeRequestGate.ActiveRaids` are **in memory**: a server restart empties them.

**Server routes that change teammates or gear** (`FriendlyTeammateStaticRouter` and others): `teammate/create`,
`teammate/delete` (mails the equipped kit back), `teammate/profile/loadout`, `/default-equipment`, `/buy-kit`,
`/repair-equipment`, `/insurance`, `teammate/health/heal`, `teammate/autojoin`, `squad-onboarding/deliver`,
`give-gear/insurance-cancel`, `recruitpickup`, `/singleplayer/returnitems`, `followergenerate`, `teammate/prepare`.

## What goes wrong today (SPSave 0.8.6 + Pit FireTeam 1.1.1)

1. **Duplicates on resume.** SPSave restores every saved bot, teammates included, as plain PMCs (no follower AI,
   probably hostile). If the teammates are still in the pre-raid group, Pit FireTeam also spawns a fresh squad next to
   the player. Two copies of each teammate, one of them hostile.
2. **Gear duplication between sessions.** SPSave's lock doesn't cover Pit FireTeam's routes. While a save waits, the
   player can delete a teammate (kit mailed back) or change their loadout, and the resume brings the saved kit back too.
3. **Lost protection after a server restart.** `ProtectedRaidItemIds` is empty after a restart, so at the final raid
   end items handed to teammates could stay in the player's profile as well.

## Plan (riskiest first)

1. **This check.** Done.
2. **Save:** record the player's followers (`IsSquadMate`, profile and account id, role) plus Pit FireTeam's protected
   and tracked item ids for each (`InteractableObjects.GetStoredEquipment` / `GetStoredItems`). The bots themselves are
   already in the save.
3. **Resume through Pit FireTeam's transit path:** BotRestore skips saved squadmates; before `SpawnFollowers` runs,
   `spawnMemberIds` is set to exactly the saved squadmates (no fresh squad from the group), the transit cache is filled
   from the saved profiles and `RaidTransit` is set, so Pit FireTeam spawns them with their saved gear and health,
   registers them as followers and re-registers the protected items. Then each is moved to its saved position.
   Filling an internal cache by reflection is fragile: every member is checked at startup, and a failure refuses to
   save while teammates are alive rather than producing a broken resume.
4. **Lock:** while a save waits, the SPSave server mod also refuses the Pit FireTeam routes above.
5. **Endings:** extraction and death go through Pit FireTeam's own raid end. Abandon (MIA) and Killed load the raid
   without restored bots today; teammates must at least be reported as lost or kept, not silently left as they were.
6. **In-game tests** on Factory with one saved teammate: save, resume (one teammate, following, right gear), extract
   (teammate outcome once, no duplicated items), then Abandon.

## Built (SPSave 0.9.0, server 0.8.0, 2026-11-07; not yet run in game)

Every reflected member was checked against the installed `pitFireTeam.dll` and `pitFireTeam.Server.dll` 1.1.1.

- `PitFireTeamCompat` (client): finds its members once; if any is missing, saving is refused (the raid goes on)
  rather than resuming the squad wrongly. Save: `RaidState.PitFollowers` (squadmate / bossAlly / recruit, ids,
  protected and tracked item ids). Resume: `PitSquadPatch` (postfix on `SendRaidSettings`, `Priority.Last`, after
  Pit FireTeam's own) clears its three spawn lists and the transit cache, fills `StatesByKey` with
  `TransitFollowerState(savedProfile, protected, tracked)` under the keys `TryCapture` uses, adds squadmates to
  `spawnMemberIds` and boss allies to `spawnMemberIdsBoss`, and sets `RaidTransit`. `BotRestore` skips them.
  Recruits: `ShallBeFollower` before activation, then `AddFollower(bot, boss, false, role)` once the bot is active
  (`RecruitRejoin`, at most 30 s, only during a resume).
- Abandon/Killed: no squad is spawned. Setting **Pit FireTeam → Teammates when a saved raid is abandoned or killed**
  (Joel: the player chooses): `ShareYourFate` (default) posts each squadmate as lost (`Escaped = false`) to
  `/singleplayer/pitfireteam/teammate/raid-outcomes`; `ComeHomeSafe` posts nothing (pre-raid state kept).
- Server: `PitFireTeamLock` refuses `Create`, `PrepareCreation`, `Delete`, `SetLoadout`, `SaveDefaultEquipment`,
  `BuyKit`, `RepairDefaultEquipment`, `InsureEquipment`, `Treatment` on `FriendlyTeammateCallbacks` while locked.

Known gaps: squadmates reappear next to the player (Pit FireTeam's spawn point), not where they stood; a recruit's
carried-loot list and Allegiance Scav trust aren't restored; boss allies come back through the same transit path but
are untested.

## Open questions

- Decided (Joel, 2026-11-07): recruits come back as followers; Abandon/Killed for squadmates is a player setting.
- Auto Join teammates on a fresh launch: does the pre-raid group already hold them when the reconnect screen shows?
  (Decides whether step 3 must clear the group or only `spawnMemberIds`.)
- Pit FireTeam's own handling when a raid never ends (game closed): presumably like Alt-F4, teammates keep their
  pre-raid server state. To confirm in game.
