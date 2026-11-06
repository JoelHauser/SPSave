# Step C1: server mod API and lock points (checked 2026-11-06)

Decompiled with ilspycmd from `H:\SPT4.1.X\SPT_Runtime\SPTarkov.Server.Core.dll` (4.1.6), ABPS 2.1.0
(`acidphantasm-botplacementsystem.dll`), ORBIT (`Orbit.Server.dll`), UI Fixes server, and the client
`Assembly-CSharp.dll` (0.16.9.40743). Note: the folder also has `SPTarkov.Server.Core.Patched.dll` (written
2026-11-06 16:21, differs from the original); which one the server loads is not checked yet.

## How a server mod registers

- Metadata: a `record` implementing `SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata` (`ModGuid`, `Name`, `Author`,
  `Version` (SemanticVersioning), `SptVersion = new Range("~4.1.0")`, `License`, ...).
- Everything else is DI: `[Injectable(InjectionType.Transient|Singleton, ..., TypePriority = n)]` from
  `SPTarkov.DI.Annotations`.
- Load hook: `SPTarkov.Server.Core.DI.IOnLoad.OnLoadAsync(CancellationToken)`. Order constants in `OnLoadOrder`
  (`Preload` 100000, `Routers` 400000, `PostLoad` 1000000).
- Routes: subclass `StaticRouter(JsonUtil, IEnumerable<RouteAction>)`; `RouteAction<TRequest>(url,
  (url, info, sessionId, output, ct) => ValueTask<string>)`.
- Harmony: subclass `SPTarkov.Reflection.Patching.AbstractPatch`, `GetTargetMethod()`, `[PatchPrefix]`/`[PatchPostfix]`;
  enabled by an `IOnLoad` that takes `IEnumerable<IRuntimePatch>` and calls `Enable()` on each (ABPS, UI Fixes).
- References (from ABPS's project): `SPTarkov.Server.Core`, `SPTarkov.DI`, `SPTarkov.Reflection`, `SPTarkov.Common`,
  `SemanticVersioning`, `0Harmony`, all in `SPT_Runtime`.
- Mod folder: `ModHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly())`.

## A router cannot refuse a request

`HttpRouter.HandleRouteAsync` runs **every** static router whose URL matches, in order, each receiving the previous
`output`; the last one's return is the response. ABPS hooks `/client/match/local/start` and `/end` this way, after the
core handler has already run. So a lock must be a Harmony prefix on the callback, not a router.

## Routes that change the profile out of raid

| Route | Entry point | Lock |
|---|---|---|
| `/client/game/profile/items/moving` | `ItemEventCallbacks.HandleEvents` (every inventory move, trade, flea offer, hideout, repair, insurance, quest, health, customization, notes, wishlist action) | refuse all |
| `/client/match/local/start` | `MatchCallbacks.StartLocalRaidAsync` | allow only the save's map and side (the resume uses it) |
| `/client/match/local/end` | `MatchCallbacks.EndLocalRaidAsync` | allow (the resumed raid ends here) |
| `/raid/profile/scavsave` | `InRaidCallbacks.SaveProgress` | refuse |
| `/client/prestige/obtain` | prestige (resets the profile) | refuse |
| `/client/game/profile/savage/regenerate` | scav profile | refuse (harmless, but no raids of any kind) |
| `/launcher/v2/wipe`, `/launcher/v2/remove` | launcher | wipe/remove makes the save meaningless: discard it |

Not locked: builds/presets, nickname, voice, friends, chat, mail *reading* (taking attachments is an item event).
Hideout crafts, insurance returns and trader restocks run on server timers; they only add to mail or the hideout and
are harmless.

A refused item event: `ItemEventCallbacks` returns `HttpResponseUtil.GetBody(output, code, message)`; any code other
than `NotEnoughSpace` is critical. The client's reaction (`ClientBackendSession.SendCallback`) is not checked yet.
The client menu lock (Resume / Abandon only) is the main guard; the server lock is the backstop.

## Resolving Killed / MIA: what the server needs, and what the client does first

Server: `LocationLifecycleService.EndLocalRaidAsync(sessionId, EndLocalRaidRequestData)` keeps no state from
`local/start`. It parses `ServerId` as `"<location>.<side> <stamp>"` and takes everything from `Results.Profile`
(`PmcData`), `Results.Result` (`ExitStatus`), `KillerId`, `LostInsuredItems`. Death deletes the inventory
(`InRaidHelper.DeleteInventory`) and strips FIR in the secure container; the request profile's
`Stats.Eft.Aggressor` drives the killer message.

Client, before it sends: `BaseLocalGame.GameEnd` calls `Player.OnGameSessionEnd(exitStatus, pastTime, locationId,
exitName)`, which needs a live player:
- `StatisticsManager.EndStatisticsSession`: win streak, `SessionExperienceMult` (`MatchEnd.GetMultiplier`),
  exit-status XP, `TotalSessionExperience` **added to `Info.Experience`**, `Sessions`, `ExitStatus` and `LifeTime`
  counters, kill streak, Mark of the Unknown penalty, `FillOverallCounters`.
- Every `IConditionalController.CheckExitConditionCounters`: quest conditions (including fail conditions on exit
  status) through `ConditionalBook.TestConditions`; then `ManageQuestStatusesForPveOfflineGameEnd` marks failed quests.
- Counter resets, secret-exit items removed (`method_119/120`), Cultist amulet charge (`LocalPlayer`).
Then `Health = ActiveHealthController.Store()`, `new ProfileDescriptor(Profile, FullySearchedSearchController.Instance)
.ToUnparsedData()`, `GetLostInsuredItems()`, `GetTransferItems()`, and `IEftSession.LocalRaidEnded(...)`.

The save holds `ProfileDescriptor.ToJson()` captured mid-raid, i.e. **before** all of the above. Sending it as is
would skip the session's XP, the death/MIA counters and fail-on-death quests.
