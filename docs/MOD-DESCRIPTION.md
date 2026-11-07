> This page applies to SPT version `4.1.6` (Escape from Tarkov `0.16.9.40743`)
{.is-info}

> **Testing build.** Save & Resume Raid has so far been tested in game on Factory only. Play it on a profile you can afford to lose until it is marked as released.
{.is-warning}

Save & Resume Raid lets you **save a raid, close the game and come back to the same raid later**. Your gear, your health, the loot you left behind, the bodies on the ground, the doors you opened and the bots still alive are all where you left them, and the raid clock picks up where it stopped.

It is built to feel like part of the game. Saving is a button in the pause menu, resuming uses EFT's own reconnect screen, and every window is the game's own.

## Features

- **Save & quit from the pause menu.** One button saves the raid, checks the save and closes the game.
- **Resume through EFT's reconnect screen.** Start the game, press `RECONNECT`, and you are back in the raid as soon as it loads.
- **The world comes back, not just you.** Loot, containers you searched, items you dropped, bodies, doors, the time of day, the weather and the remaining raid time are restored.
- **Living bots come back.** Every bot that was alive returns to the same spot with the same gear. With SAIN installed, each one keeps its personality.
- **Progress counts once.** XP, kills, quest progress and stats from every session of a raid are added together and applied once when the raid finally ends.
- **No duplication.** While a save is waiting, your profile is locked so nothing can be moved, sold or spent until you resume or abandon the raid.
- **No cost while you play.** Nothing runs every frame. The work happens when you save and when you resume.

## How to use

### Saving a raid

1. Press <kbd>Esc</kbd> in a raid.
2. Choose `SAVE & QUIT`, just below `DISCONNECT`.
3. The raid is saved and checked, and the game closes. Your SPT server can keep running.

### Resuming a raid

1. Start the game as usual.
2. The main menu opens on the reconnect screen for your saved raid.
3. Press `RECONNECT`. The raid loads and you have control as soon as the loading screen fades.

### Abandoning a raid

1. On the reconnect screen, press `LEAVE`.
2. Confirm. The raid ends as **Missing in Action**: you lose the gear you took in, exactly as with a normal MIA.

## Saving in combat

Saving is allowed at any time, but **saving during a fight counts as dying**. If a bot can see you, or has seen or shot at you in the last 30 seconds, you are asked to confirm first. If you go ahead:

- The next time you start the game, the reconnect screen tells you that you quit during combat and only offers `LEAVE`.
- The raid ends as **Killed in Action**, with the normal death screens, XP and loss of gear.

The button can't be used to escape a firefight.

## What is restored

| Restored exactly | Rebuilt | Not restored |
| - | - | - |
| Your inventory, magazines, chambered rounds, durability and found-in-raid status | Bleeds, fractures, painkillers and other effects, with their timers | Bullets and grenades in flight |
| Health per body part, energy and hydration | Your position, facing, stance and stamina | What a bot was doing at that moment |
| Searched containers, dropped items, bodies and their loot | Living bots, in place and fully geared, but unaware of you | BTR, airdrops, artillery, transits and seasonal events |
| Doors and switches | Later spawn waves (waves that already came don't come again) | Sounds |
| Raid time left, time of day and weather | | |

## Requirements

- SPT `4.1.6`
- Both parts of this mod: the client plugin **and** the server mod. The client alone can't lock your profile, so it won't save.

## Installation

1. Close the game and the SPT server.
2. Extract the archive into your SPT folder, so that the files land in:
   - `[game folder]\BepInEx\plugins\SPSave.dll`
   - `[game folder]\SPT_Runtime\user\mods\SPSave\SPSave.Server.dll`
3. Start the server, then the game.

Saves are kept in `[game folder]\SPSave\saves\`, one folder per profile. Only the three most recent used-up saves are kept for troubleshooting; older ones are deleted automatically.

## How your save can be lost

> **Read this before you rely on a save.** In every case below, your profile goes back to how it was **before you entered the raid**: you keep the gear you took in, and lose everything gained in that raid across all its sessions. Nothing is ever duplicated.
{.is-danger}

| What happens | Result | How to avoid it |
| - | - | - |
| The game crashes, or you close it with <kbd>Alt</kbd>+<kbd>F4</kbd>, **after** resuming | The save was used up when you resumed, so the raid is lost, exactly as in standard SPT. | Only leave a raid with `SAVE & QUIT`, extraction or death. |
| Escape from Tarkov is updated (SPT updates often bring a new game version) | A save from another game version can't be loaded. The reconnect screen only offers `LEAVE`, which discards it. | Finish or abandon saved raids before updating SPT. |
| A mod is removed or updated while a save is waiting | You are warned and can still resume. If the save holds items from a mod that is gone, or a mod changed too much, the raid may not load. You then get a fresh raid on the same map and the save is discarded. | Don't change your mods while a save is waiting. |
| The SPSave server mod is removed, or the lock in `\SPT_Runtime\user\mods\SPSave\locks` is deleted | The save no longer matches your profile on the server and is discarded the next time you start the game. | Leave the server mod and its `locks` folder alone. |
| The save in `\SPSave\saves` is deleted or moved | The raid can't be resumed; your profile is unlocked the next time you start the game. | Leave the `SPSave` folder alone, and move it with your SPT folder. |
| The profile is wiped or deleted from the launcher | The save is discarded with it. | – |

Two things that **don't** lose your raid:

- If the server isn't running or the server mod is missing when you press `SAVE & QUIT`, the save is refused and **your raid simply carries on**.
- If the save doesn't pass its own check, it is discarded and **your raid carries on**.

## Uninstalling

> **Resume or abandon your saved raid first.** If you remove the mod while a save is waiting, your profile stays locked on the server: traders, the flea market, your stash, the hideout and mail refuse every action.
{.is-warning}

If that has already happened:

1. Close the game and the SPT server.
2. Delete your profile's file in `\SPT_Runtime\user\mods\SPSave\locks`.
3. Start the server. Your profile is unlocked and stays as it was before the saved raid.

Also leave `Resume saved raids` in the F12 menu turned on while a save is waiting. Turning it off hides the save but keeps the profile locked.

## Compatibility

- **SAIN:** supported. Restored bots keep their personality.
- **ABPS:** supported. Restored PMCs are recognised, and its PMC waves carry on after a resume.
- **ORBIT:** supported, including bots it has put to sleep.
- **Other mods:** if your client mods change between saving and resuming, the reconnect screen names them and asks before loading. Mods added afterwards are allowed.

## Known limitations

- **PMC raids only.** Don't save Scav raids.
- **Solo only.** Not for co-op or Fika.
- Restored bots don't remember you. A raid saved mid-fight ends as **Killed**, so this only matters for bots that were hunting you quietly.
- A boss that was about to spawn when you saved may not appear.
- Distance walked and time online for that session are not carried over to your stats. Kill times count from the last resume.

## Reporting a problem

Please include:

- `[game folder]\BepInEx\LogOutput.log`, where every line from this mod starts with `[SPSave]`.
- The SPT server console output, where this mod's lines start with `[SPSave]`.
- The map, what you did, and your list of installed mods.

## Credits

- Freezing the raid while it loads follows the method of the **Pause** mod.
- Thanks to the SPT team for SPT and its modding tools.
