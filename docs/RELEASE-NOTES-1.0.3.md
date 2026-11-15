**For SPT 4.1.6** (Escape from Tarkov 0.16.9.40743). Solo PMC raids.

### What's new
- **Autosave (optional, off by default):** SPSave can now save your raid in the background every few minutes, so a game crash doesn't cost you the raid. After a crash, start the game as usual and press RECONNECT to carry on from the last autosave. Turn it on in the F12 menu under "Saving" → "Autosave every (minutes)" (0 = off, up to 60).
  - It doesn't autosave in combat. The last autosave before the fight stays.
  - Closing the game normally (Alt-F4 or the window's X) still scraps the raid, like standard SPT. Only a crash leaves the autosave to resume.
  - Each autosave freezes the game for a moment, the same as SAVE & QUIT does, and longer on big maps like Streets. Pick an interval you're comfortable with.
  - While an autosave exists, your profile is locked on the server, the same as for a waiting save. Other mods that change your stash or trade with traders during a raid may be refused until the raid ends.

### What's fixed
- **Actually Found In Raid:** saves made with Actually Found In Raid installed were always thrown away ("the save didn't check out"). That mod lets ammo be found in raid, and SPSave's save check didn't expect it. Saves now work with it, and found-in-raid ammo stays found in raid after you resume. Thanks to CrossWrap for the report and the log.

### Updating from 1.0.2, 1.0.1 or 1.0.0
1. **Finish or abandon any saved raid first.** Press RECONNECT and play it out, or press LEAVE.
2. **Close the game and the SPT server.** The server keeps its DLL open while it runs, so the copy fails or doesn't take effect.
3. **Extract `SPSave-1.0.3.zip` into your SPT folder** and let it overwrite both files:
   - `BepInEx/plugins/SPSave.dll`
   - `SPT_Runtime/user/mods/SPSave/SPSave.Server.dll`
4. **Start the server, then the game.**

Update both files together; the client and the server are released as a pair.

### New install
Extract `SPSave-1.0.3.zip` into your SPT folder and restart the server.

### Works with
SAIN, ABPS, ORBIT, Pit FireTeam, Menu Overhaul, BossNotifier, Raid Review and Actually Found In Raid.

Full details: [README](https://github.com/JoelHauser/SPSave#readme).
