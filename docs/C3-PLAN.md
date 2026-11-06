# Step C3: lock, menu and storage (plan, 2026-11-06)

Everything below was read in decompiled code (client 0.16.9.40743, server 4.1.6). Nothing is built yet.
Joel's rule: all UI must look like stock EFT, so this reuses the game's own reconnect flow instead of new UI.

## Menu: EFT's own "raid in progress" flow

Live EFT already has a menu for "you have an unfinished raid" (profile status `Busy`, `TarkovApplication` ~line 2179).
SPT never reaches it (local raids throw there), but every piece exists:

1. `TarkovApplication.MainMenu(Action reconnectAction)`. When `reconnectAction` isn't null,
   `MainMenuShowOperation.ShowMenuScreen` shows `MenuScreen.MainMenuReconnectScreenController` instead of the normal one:
   - `TaskBarButtonsAvailability = Disabled`: the bottom bar (traders, flea, messenger, hideout, ...) is disabled;
   - `MenuScreen.ChangeToReconnectionAvailable(true)`: Trade, Hideout, Character and the PvE/PvP toggle hidden, the
     warning panel shown;
   - the play button selects `EMenuType.Reconnect`, which calls `reconnectAction`.
2. Live EFT's `reconnectAction` shows `ReconnectionScreen` (`new ReconnectionScreenController(profile, location, side,
   returnAllowed: false, nextScreenAllowed: true, session)`). It shows the map name, level, character model and the
   map's banners, with **Reconnect** (`OnReconnectAction`) and **Leave** (`OnLeave`). All labels are the game's.

SPSave: a prefix on `TarkovApplication.MainMenu` passes our own `reconnectAction` when the profile has a save.
- Reconnect = Resume: fill the menu's `RaidSettings` from the manifest (`SelectedLocation` from
  `Session.LocationSettings.locations`, `Side = Pmc`, `SelectedDateTime` from `TimeVariant`), then run the game's own
  Ready handler `MainMenuShowOperation.method_27()` (validates, applies settings, flushes the queue, raises
  `OnReadyToStartMatching` → `OnReadyToStartMatchingAsync` → `LocalGameMatching`, where the resume already hooks in).
  To check when building: the Local/PvE flags SPT sets on the settings, and what `method_54`/`method_58` test.
- Leave = Abandon: a stock confirmation (`ItemUiContext.ShowMessageWindow(text, accept, cancel, caption)`, as
  `MenuScreen.EnsureToggleGameMode` does), then `ResumeSession.Requested = MissingInAction` and the same start as
  Resume. That ends the raid as MIA through C2 (screen black throughout).
- A combat save: Reconnect also ends it as Killed (`ResolveRule`); see the question below.

## Server lock (backstop; the menu is the main guard)

- New server mod (net10.0; .NET SDK 10.0.401 is installed), registered as ABPS does (C-SERVER-API.md).
- State: one small lock per profile, written by the server mod in its own folder (`user/mods/SPSave/locks/<id>.json`).
  Not in `user/profiles`: the server loads profiles from there.
- Routes: `/spsave/lock` (Save & Quit, before the game closes; the save is refused if the server doesn't confirm),
  `/spsave/unlock` (a save is used up on Ready or resolved; a normal raid end discards the save).
- Harmony prefixes refuse while locked: `ItemEventCallbacks.HandleEvents` (every item event), `local/start` on another
  map or side, `scavsave`, `prestige/obtain`, `savage/regenerate`. Launcher wipe/remove drops the lock.
- `SPTarkov.Server.Core.Patched.dll` is the prepatched core that `ModLoader` rebuilds at boot when a mod has a
  prepatcher (SkillsExtended here). The server runs it in memory; ABPS's Harmony patches work under that setup, so ours will.
- Still open: how the client shows a refused item event (`ClientBackendSession.SendCallback`). With the menu locked,
  only other mods' calls could reach it.

## Decisions (Joel, 2026-11-06)

1. Storage: the save stays at the SPT root (`<SPT>\SPSave\saves\<profile>\raid.spsave`, as now); only the lock
   lives on the server.
2. A combat save (ends as Killed) shows only LEAVE on the reconnect screen (`nextScreenAllowed: false` hides
   RECONNECT); LEAVE ends it as Killed.
3. Abandon confirmation, in the stock dialog: "Abandon this raid? You will be listed as Missing in Action and lose
   the gear you took in."
