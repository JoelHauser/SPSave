# Save & Resume Raid

Real life doesn't wait for extract. Save & Resume Raid lets you **save a PMC raid, close the game and pick it up later**, right where you left off: same spot, same gear, same loot, same bots.

It's built to feel like part of the game: saving is a button in the pause menu, resuming uses EFT's own reconnect screen, and every window is the game's own.

## How to use

- **Save:** press <kbd>Esc</kbd> in a raid and choose `SAVE & QUIT`.
- **Resume:** start the game and press `RECONNECT`.
- **Abandon:** press `LEAVE` on the reconnect screen. The raid ends as *Missing in Action*.

**Saving during a fight counts as dying.** You're asked first, and the raid ends as *Killed*.

While a save is waiting, your profile is locked (no trading, flea, stash or other raids), so nothing can be duplicated. XP, kills and quest progress count once, when the raid really ends.

## ⚠ Don't change your mods while a save is waiting

**Finish or abandon the raid first.** If you remove or update a mod (especially one that adds items or gear), or update SPT, the saved raid may not load. Your profile then goes back to how it was before the raid, and the raid is lost.

Crashing or pressing <kbd>Alt</kbd>+<kbd>F4</kbd> *after* resuming also loses the raid, as in standard SPT. Adding new mods is fine.

## Installation

Both parts of the mod are needed.

1. Close the game and the SPT server.
2. Extract the archive into your SPT folder, so that the files land in:
   - `[game folder]\BepInEx\plugins\SPSave.dll`
   - `[game folder]\SPT_Runtime\user\mods\SPSave\SPSave.Server.dll`
3. Start the server, then the game.

## Uninstalling

**Resume or abandon any saved raid before uninstalling**, or your profile stays locked. If that has already happened:

1. Close the game and the SPT server.
2. Delete your profile's file in `[game folder]\SPT_Runtime\user\mods\SPSave\locks`.
3. Start the server.

## Compatibility

- **Works with:** SAIN, ABPS, ORBIT, Pit FireTeam and Menu Overhaul.
- **Not for:** Fika or co-op. Solo play only.

## Known limitations

- PMC raids only.
- Restored bots don't remember you.
- BTR, airdrops, artillery and seasonal events aren't restored. Halloween zombies are untested.

## FAQ

### Can I close the SPT server too?

Yes. The save and the lock are kept on disk, so you can shut everything down and come back days later.

### Does time pass while I'm away?

No. The raid clock, time of day and your health pick up exactly where they stopped.

### Can I save the same raid more than once?

Yes. A resumed raid can be saved again as often as you like.

### Can I have several saved raids?

No, one per profile. Resume or abandon it before starting another raid.

### Will the bots remember me?

No. They come back where they were, with their gear, but unaware of you.

### Does it slow down my game?

No. Nothing runs while you play; the work happens only when you save and when you resume.

### Why did I get a fresh raid instead of my save?

The save couldn't be rebuilt, usually because a mod was removed or changed while it was waiting. See the warning above about changing mods.

### My traders and flea say I'm in a raid

A save is waiting: resume or abandon it. If you uninstalled the mod first, see [Uninstalling](#uninstalling).

## Reporting a problem

Send these **before starting the game again**, because each start overwrites them:

- `[game folder]\BepInEx\LogOutput.log`, where this mod's lines start with `[SPSave]`.
- The SPT server console output.
- The map and what you did.
