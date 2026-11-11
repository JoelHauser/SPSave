# Save & Resume Raid

> *Tarkov has no pause button. Real life never got the memo.*

The pizza guy is at the door, the baby just woke up, your boss is calling, and you're 25 minutes into the best raid of the week. Now you don't have to choose: **save the raid, deal with life, and come back to it later**, right where you left off.

**Version 1.0.2** for **SPT 4.1.6** (Escape from Tarkov 0.16.9.40743). Solo PMC raids.

---

## Features

| Feature | What it means |
| - | - |
| **Save anytime** | One button in the pause menu saves the raid and closes the game. |
| **The whole raid comes back** | Your position, gear and health, dropped loot, bodies, opened doors, time of day, weather and the raid time left. |
| **Living bots return** | Every bot that was alive comes back to its spot with its gear. They just don't remember you. |
| **Progress counts once** | XP, kills and quest progress from every session are added up when the raid really ends. |
| **No exploits** | While a save is waiting, your profile is locked: no trading, flea, stash or other raids. |
| **Feels stock** | Uses EFT's own pause menu, reconnect screen and windows. No performance cost while you play. |

---

## Quick start

| Action | How |
| - | - |
| **Save** | In a raid, press <kbd>Esc</kbd> and choose `SAVE & QUIT`. |
| **Resume** | Start the game and press `RECONNECT`. |
| **Abandon** | Press `LEAVE` on the reconnect screen. The raid ends as *Missing in Action*. |

> **Saving during a fight counts as dying.** You're asked first, and the raid ends as *Killed*. It's not an escape button.

---

## ⚠ Important: while a save is waiting

> **Finish or abandon the raid before changing anything.** If a save can't be loaded, your profile goes back to how it was before the raid and the raid is lost.

| Don't | Why |
| - | - |
| Remove or update mods | Items from a missing or changed mod (weapons, gear, containers) may not load. *Adding* mods is fine. |
| Update SPT | A save from another game version can't be loaded. |
| Uninstall this mod | Your profile stays locked. See *Uninstalling*. |
| Delete or move `[game folder]\SPSave` | That's where your save lives. |
| Crash or <kbd>Alt</kbd>+<kbd>F4</kbd> after resuming | The save is used up once the raid loads, so the raid is lost, as in standard SPT. |

If `SAVE & QUIT` itself fails, nothing is lost: **your raid simply carries on.**

---

## Installation

**Requires both parts of the mod.**

1. Close the game and the SPT server.
2. Extract the archive into your SPT folder, so that the files land in:
   - `[game folder]\BepInEx\plugins\SPSave.dll`
   - `[game folder]\SPT_Runtime\user\mods\SPSave\SPSave.Server.dll`
3. Start the server, then the game.

## Updating

1. **Finish or abandon any saved raid first:** press `RECONNECT` and play it out, or press `LEAVE`.
2. Close the game and the SPT server. The server keeps its file open while it runs.
3. Extract the new archive into your SPT folder and let it **overwrite both files**. Always update both together.
4. Start the server, then the game.

## Uninstalling

**Resume or abandon any saved raid first.** If your profile is already stuck locked:

1. Close the game and the SPT server.
2. Delete your profile's file in `[game folder]\SPT_Runtime\user\mods\SPSave\locks`.
3. Start the server.

---

## Compatibility

| | Mods |
| - | - |
| ✅ **Works with** | SAIN, ABPS, ORBIT, Pit FireTeam, BossNotifier, Raid Review |
| ❌ **Not for** | Fika or co-op (solo play only) |

| Mod | What SPSave does with it |
| - | - |
| **SAIN** | Bots that come back keep their personality. |
| **ABPS** | Its PMC waves carry on from where the raid was, and returning PMCs count toward its spawn spacing. |
| **ORBIT** | Sleeping bots are saved and come back too. |
| **Pit FireTeam** | Your squadmates come back at their spots. If you abandon the raid or die, F12 decides whether they're lost too. |
| **BossNotifier** | After a resume it lists the bosses your saved raid actually had. |
| **Raid Review** | A saved raid is recorded as **one raid**: both halves on one timeline, however long you were away. |

## Known limitations

- PMC raids only.
- BTR, airdrops, artillery and seasonal events aren't restored. Halloween zombies are untested.

---

## FAQ

**Can I close the SPT server too?**
Yes. You can shut everything down and come back days later.

**Does time pass while I'm away?**
No. The raid clock, time of day and your health pick up exactly where they stopped.

**Can I save the same raid more than once?**
Yes, as often as you like.

**Why did I get a fresh raid instead of my save?**
The save couldn't be rebuilt, usually because a mod was removed or changed while it was waiting.

**My traders and flea say I'm in a raid.**
A save is waiting: resume or abandon it.

**Where's my raid in Raid Review?**
It shows once, dated from when you resumed it, with the whole raid from start to finish. If a save is thrown away (for example, after a game update), the part played before the save shows as a finished raid.

---

## Changelog

**1.0.2**
- Pause menu: `SAVE & QUIT` and `RESUME` stay in their column when the screen resolution changes during a session (for example, switching an ultrawide to 16:9). Before, `SAVE & QUIT` jumped to the right of `DISCONNECT` and `RESUME` went off screen.

**1.0.1**
- Raid Review: a saved raid is recorded as one raid, not just the part after the resume.
- BossNotifier: shows the saved raid's bosses after a resume, not ones the raid never had.
- No more log errors for bodies brought back by a resume.
- F12 has two settings, under *Saving* and *Pit FireTeam*.

**1.0.0**
- First release.

---

## Reporting a problem

Send these **before starting the game again**, because each start overwrites them:

- `[game folder]\BepInEx\LogOutput.log`
- The SPT server console output
- The map and what you did
