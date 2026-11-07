> Version `1.0.0` for SPT `4.1.6` (Escape from Tarkov `0.16.9.40743`)
{.is-info}

You're 30 minutes into Streets with a backpack full of GPUs and a Killa-free route to extract. Your wife calls you to dinner. Then she calls you again. The third time it isn't a question.

Press `SAVE & QUIT`, go be a good husband, and come back at 2 a.m. when she's asleep. Your raid is waiting exactly where you left it: same spot, same gear, same loot on the ground, same scav still camping that corner.

**Save & Resume Raid** lets you save a PMC raid, close the game and pick it up later. It's built to feel like part of the game: saving is a button in the pause menu, resuming uses EFT's own reconnect screen, and every window is the game's own.

## What it does

- **Save from the pause menu.** `SAVE & QUIT` saves the raid, checks the save and closes the game.
- **Resume through the reconnect screen.** Start the game, press `RECONNECT`, and you're back in as soon as it loads.
- **The whole raid comes back.** Your gear, health and effects, loot you dropped or left in containers, bodies, doors, time of day, weather and the raid time left.
- **Bots come back too.** Every bot that was alive returns to its spot with its gear (SAIN personalities included). They don't remember you, though.
- **Progress counts once.** XP, kills and quest progress from every session of a raid are added up and applied once, when the raid really ends.
- **No duplication.** While a save is waiting, your profile is locked: no trading, flea, stash, hideout or other raids until you resume or abandon it. Daily and weekly quests don't rotate either.
- **No cost while you play.** Nothing runs every frame. The work happens when you save and when you resume.

## How to use

**Save:** in a raid, press <kbd>Esc</kbd> and choose `SAVE & QUIT` (under `LEAVE`). The game closes; your SPT server can keep running.

**Resume:** start the game. The main menu opens in reconnect mode. Press `RECONNECT`.

**Abandon:** on the reconnect screen, press `LEAVE` and confirm. The raid ends as **Missing in Action**, with the normal results screens.

**Saving in combat counts as dying.** If a bot sees you, or has seen or shot at you in the last 30 seconds, you're asked first. Save anyway and the raid ends as **Killed** next time you start the game. It's not an escape button.

## ⚠ Things that break a save

> **Don't change anything while a save is waiting.** Finish or abandon the raid first. If a save can't be loaded, your profile goes back to how it was **before you entered the raid**: you keep the gear you took in and lose everything you gained in that raid. Nothing is duplicated, but the raid is gone.
{.is-danger}

| Don't do this while a save is waiting | What happens |
| - | - |
| **Remove a mod**, especially one that adds items, weapons, gear or bots | The reconnect screen warns you and names the mod. If you resume anyway and the save holds that mod's items, the raid can't be rebuilt: you get a fresh raid on that map and the save is thrown away. |
| **Update a mod** | Same warning. Small updates usually load fine; big ones can fail like a removed mod. |
| **Update SPT** (it usually brings a new EFT version) | A save from another game version can't be loaded at all. `LEAVE` is the only option, and it discards the save. |
| **Remove SPSave** (either part) | Your profile stays locked on the server: traders, flea, stash and hideout refuse everything. See *Uninstalling* below. |
| **Delete or move** `SPSave\saves` or `SPT_Runtime\user\mods\SPSave\locks` | The save and your profile no longer match, and the save is discarded. |
| **Crash or <kbd>Alt</kbd>+<kbd>F4</kbd> after resuming** | The save was used up when the raid loaded, so the raid is lost, as in standard SPT. Leave only with `SAVE & QUIT`, extraction or death. |

Adding new mods is fine.

If `SAVE & QUIT` fails (server not running, server mod missing, or the save doesn't pass its check), nothing is lost: **your raid simply carries on.**

## Requirements and installation

- SPT `4.1.6`
- **Both parts** of this mod: the client plugin and the server mod. Without the server mod it won't save.

1. Close the game and the SPT server.
2. Extract the archive into your SPT folder, so the files land in:
   - `[game folder]\BepInEx\plugins\SPSave.dll`
   - `[game folder]\SPT_Runtime\user\mods\SPSave\SPSave.Server.dll`
3. Start the server, then the game.

Saves live in `[game folder]\SPSave\saves\`, one folder per profile. The last three used-up saves are kept for troubleshooting.

## Uninstalling

> **Resume or abandon your saved raid first.**
{.is-warning}

If you removed the mod with a save still waiting and your profile is stuck locked:

1. Close the game and the SPT server.
2. Delete your profile's file in `[game folder]\SPT_Runtime\user\mods\SPSave\locks`.
3. Start the server. Your profile is unlocked, as it was before the saved raid.

## Compatibility

- **SAIN:** restored bots keep their personality.
- **ABPS:** restored PMCs are recognised, and its PMC waves carry on after a resume.
- **ORBIT:** supported, including bots it has put to sleep.
- **Pit FireTeam:** your squadmates come back at their saved spots. If you abandon the raid, the F12 setting decides whether they're lost with you (default) or come home safe.
- **Menu Overhaul:** supported.

## Known limitations

- **PMC raids only**, solo only (no Fika or co-op). The button is hidden in Scav raids.
- Restored bots start fresh: they don't remember seeing you.
- Not restored: BTR, airdrops, artillery, transits, bullets in flight, and what a bot was doing at that moment.
- Seasonal events aren't saved: the Halloween summoning event may start over after a resume. Halloween zombies are **untested**.
- A boss that was about to spawn when you saved may not appear.
- Distance walked and time online for that session aren't added to your stats.

## Reporting a problem

Send these **before starting the game again**, because each start overwrites them:

- `[game folder]\BepInEx\LogOutput.log` (this mod's lines start with `[SPSave]`)
- The SPT server console output (lines start with `[SPSave]`)
- The map, what you did, and your list of mods

## Credits

- Shifting the raid clock after loading follows the method of the **Pause** mod.
- Thanks to the SPT team for SPT and its modding tools.
