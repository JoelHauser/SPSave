# Save & Resume Raid

> *Tarkov has no pause button. Real life never got the memo.*

The pizza guy is at the door, the baby just woke up, your boss is calling, and you're 25 minutes into the best raid of the week. Now you don't have to choose: **save the raid, deal with life, and come back to it later**, right where you left off.

**Version 1.1.0** for **SPT 4.1.6** (Escape from Tarkov 0.16.9.40743). Solo PMC raids.

---

## Features

| Feature | What it means |
| - | - |
| **Save anytime** | One button in the pause menu saves the raid and closes the game. |
| **Autosave** *(optional, off by default)* | Saves the raid in the background every few minutes, so a game crash doesn't cost you the raid. |
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
| **Turn on autosave** | <kbd>F12</kbd> → *Saving* → *Autosave every (minutes)*. `0` is off (the default), up to `60`. |
| **After a crash** | Start the game as usual and press `RECONNECT`: the raid carries on from the last autosave. |

> **Saving during a fight counts as dying.** You're asked first, and the raid ends as *Killed*. It's not an escape button.

### Autosave

**Autosave is off until you turn it on.** It's for crashes: with it on, a crash costs you at most a few minutes of the raid instead of the whole raid.

- **It skips fights.** No autosave happens while you're in combat; the last one from before the fight stays.
- **Closing the game normally still scraps the raid**, as in standard SPT: <kbd>Alt</kbd>+<kbd>F4</kbd> or the window's X throws the autosave away. Only a crash leaves it to resume. Ending the game in Task Manager counts as a crash.
- **Each autosave costs a short hitch** (about a tenth of a second on Woods); the rest of the work runs in the background. Pick an interval you're comfortable with.
- **While an autosave exists, your profile is locked**, as for a waiting save. Other mods that sell or trade with the server during a raid may be refused until the raid ends.

---

## ⚠ Important: while a save is waiting

> **Finish or abandon the raid before changing anything.** If a save can't be loaded, your profile goes back to how it was before the raid and the raid is lost.

| Don't | Why |
| - | - |
| Remove or update mods | Items from a missing or changed mod (weapons, gear, containers) may not load. *Adding* mods is fine. |
| Update SPT | A save from another game version can't be loaded. |
| Uninstall this mod | Your profile stays locked. See *Uninstalling*. |
| Delete or move `[game folder]\SPSave` | That's where your save lives. |
| Crash or <kbd>Alt</kbd>+<kbd>F4</kbd> after resuming | The save is used up once the raid loads, so the raid is lost, as in standard SPT. With autosave on, a crash resumes from the last autosave instead. |

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
| ✅ **Works with** | SAIN, ABPS, ORBIT, Pit FireTeam, BossNotifier, Raid Review, Actually Found In Raid |
| ❌ **Not for** | Fika or co-op (solo play only) |

| Mod | What SPSave does with it |
| - | - |
| **SAIN** | Bots that come back keep their personality. |
| **ABPS** | Its PMC waves carry on from where the raid was, and returning PMCs count toward its spawn spacing. |
| **ORBIT** | Sleeping bots are saved and come back too. |
| **Pit FireTeam** | Your squadmates come back at their spots. If you abandon the raid or die, F12 decides whether they're lost too. |
| **BossNotifier** | After a resume it lists the bosses your saved raid actually had. |
| **Raid Review** | A saved raid is recorded as **one raid**: both halves on one timeline, however long you were away. |
| **Actually Found In Raid** | Ammo it marks found in raid stays found in raid after a resume: in your magazines, chambers and pockets, on bodies and on bots. |

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

**Does autosave slow the game down?**
Turned off, it costs nothing. Turned on, each autosave is a short hitch (about a tenth of a second on Woods, more on big maps like Streets); everything else happens in the background.

**I crashed with autosave on. What do I get back?**
The raid as it was at the last autosave: your spot, gear, health, the bots and the clock. Anything after that autosave didn't happen.

**Why did I get a fresh raid instead of my save?**
The save couldn't be rebuilt, usually because a mod was removed or changed while it was waiting.

**My traders and flea say I'm in a raid.**
A save is waiting: resume or abandon it.

**Where's my raid in Raid Review?**
It shows once, dated from when you resumed it, with the whole raid from start to finish. If a save is thrown away (for example, after a game update), the part played before the save shows as a finished raid.

---

## Changelog

**1.1.0**
- **Autosave (optional, off by default):** saves the raid in the background every few minutes, so a crash can be resumed with `RECONNECT`. <kbd>F12</kbd> → *Saving* → *Autosave every (minutes)*. Skips fights; a normal quit still scraps the raid.
- **Actually Found In Raid:** saves made with it installed were always thrown away ("the save didn't check out"). They work now, and its found-in-raid ammo stays found in raid after a resume. Thanks to CrossWrap for the report.
- **Raid Review:** fixed an SPT server crash about a minute after saving with Raid Review installed.
- The first save of a session is faster.

**1.0.2**
- Pause menu: `SAVE & QUIT` and `RESUME` stay in their column when the screen resolution changes during a session (for example, switching an ultrawide to 16:9). Before, `SAVE & QUIT` jumped to the right of `DISCONNECT` and `RESUME` went off screen.

**1.0.1**
- Raid Review: a saved raid is recorded as one raid, not just the part after the resume.
- BossNotifier: shows the saved raid's bosses after a resume, not ones the raid never had.
- No more log errors for bodies brought back by a resume.
- F12 settings under *Saving* and *Pit FireTeam*.

**1.0.0**
- First release.

---

## Reporting a problem

Send these **before starting the game again**, because each start overwrites them:

- `[game folder]\BepInEx\LogOutput.log`
- The SPT server console output
- The map and what you did
