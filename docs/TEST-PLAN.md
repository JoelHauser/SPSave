# B2 step 2: living bots come back (0.3.1)

0.3.0 run 2026-11-06 (Factory): 5/5 bots back at their exact saved positions, 3 PMCs known to ABPS, fresh spawns
held, no errors. It cleared the SAIN pins on Ready, so personalities weren't kept; 0.3.1 fixes that and logs it.

About 6 minutes, no fighting needed. Build with `-p:DeployToSPT=true`. Saves made with 0.2.0 still load, but their
bots come back without SAIN personality or squad numbers, so make a fresh one.

**First raid, Factory (day), naked PMC, bots on as normal:**

1. Stay hidden for about 2 minutes so scavs spawn and spread out. If you can see one without it seeing you, note
   where it is and what it carries.
2. Out of contact, press **Ctrl+F10**. If it says you're in combat, wait and try again.

**Relaunch the game and start Factory (day).** It loads frozen as in B1.

3. Before pressing anything: the scav you noted stands where it was, with the same gear.
4. Press **Ctrl+F11**. Let a scav find you, or shoot one: it must react (fire back, take cover). That checks that
   restored bots get hit events.
5. Leave.

**Send back** every `SPSave` line from `BepInEx\LogOutput.log`. The ones that matter:
- `Resuming the raid ... N/N living bots`: every saved bot rebuilt.
- `Raid restored: ... N/N bots spawning`.
- One `Bot back:` line per bot: position against saved position, health, saved SAIN personality.
- One `SAIN personality kept for <bot>: <personality>` line per bot, usually after Ctrl+F11 (when SAIN activates
  it). It must match that bot's saved personality. A bot with none of these lines kept nothing.
- `Holding fresh bot spawns`: expected. No new waves in a resumed raid until step 3.
- Any `Restoring bot ... failed` is a bug.

# B1: resume your character and the world (no bots)

About 10 minutes. **Ctrl+F10 is Save & Quit**: it saves, checks the save, and closes the game. The server never
hears the raid end, so there is no MIA and your kit is safe. It refuses while you're in combat (for now).

**First raid, Factory (day), bots on as normal:**

1. Loot something. Open a door that starts closed. Kill a scav if one comes along. Firing part of a magazine and
   taking a bleed are a bonus.
2. Get out of contact, note the raid timer, then press **Ctrl+F10**. The game closes. The server can keep running.

**Relaunch the game and start Factory (day).** You should get "SPSave: resuming your saved raid on factory4_day",
then the game loads frozen with "Raid restored. Everything is paused."

3. **Before pressing anything:** same spot and facing; the door open; the scav's body where it fell; the timer showing
   the time you noted. No bots (B1 doesn't restore them).
4. Press **Ctrl+F11** to take control. Check the magazine and your health.
5. Extract or Leave, then "check the logs". The log checks found-in-raid for you.

Extracting, dying or leaving discards any save made in that raid, so the next Factory raid is fresh. A save is also
used up when you press Ctrl+F11.

# Step A: in-game checks

Step A captures a raid to disk and reads it back. It restores nothing and never ends the raid, so a raid with
captures in it must play and extract exactly like one without.

## Setup

```
dotnet build src\SPSave -c Release -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true
dotnet build tools\FrameProbe -c Release -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true
```

Capture key: **Ctrl+F10** (BepInEx config `com.joelhauser.spsave.cfg`). Output goes to `H:\SPT4.1.X\SPSave\`:
`saves\<profile id>\raid.spsave`, `captures.csv`, and `frameprobe\`.

To look inside a save:

```
dotnet run -c Release --project tools\SaveDump -- H:\SPT4.1.X\SPSave\saves\<profile id>\raid.spsave <out dir>
```

## A1. Capture works (Factory)

1. Start a raid. Before any contact, press Ctrl+F10. Expect "SPSave: captured in N ms, N KB".
2. Kill a scav. Loot a container (take everything). Fire part of a magazine. Drop one item on the floor. Capture again.
3. Dump the save. In `state.json` check:
   - player position, pose and hands item match what you were doing
   - the dead scav is not in `Bots`; a living one is
   - effects list any bleed or fracture you have, with times
   - `Clock`: remaining time matches the raid timer; `GameTime` matches the in-game clock
4. Repeat steps 1 and 3 once on an outdoor map (Customs). Factory has no weather controller, so only an outdoor map
   checks weather: the log line should say "weather N nodes", not "none on this map" or "NODES MISSING".
5. In the BepInEx log, the "Captured" line says `round trip ok`, and `captures.csv` has `round_trip_ok` 1.
6. Finish the raid normally (extract). The profile must look exactly as it would without the mod.

## A2. Combat check

1. Let a scav see you and shoot at you. Press Ctrl+F10 during the fight. Expect "(in combat)".
2. Break contact and wait more than 30 s out of sight. Capture again. Expect no "(in combat)". The log's combat
   detail counts every hostile bot as "hostile without contact": each one has an enemy entry for you from spawn.
3. Note anything that felt wrong: a capture that said "in combat" when you weren't, or the reverse.

## A3. Save time on a busy map

On Streets or Lighthouse, mid-raid with bodies on the ground, capture three times. `captures.csv` has the frame time
and each phase. This is the hitch a real save will cost.

## A4. Normal-play cost (the number that matters most)

FrameProbe stays installed for every run; it records each raid frame after a 30 s warm-up and writes
`frameprobe\summary.csv` when the raid ends. Do **not** press Ctrl+F10 in these runs.

1. Remove `SPSave.dll` from `BepInEx\plugins`. Play Customs for about 15 minutes on a fixed route. Extract.
2. Put `SPSave.dll` back. Same map, same route, same length.
3. Repeat both once more, alternating (off, on, off, on). Set "Run label" in the FrameProbe config to the route if it changes.

Compare the rows: `spsave_loaded` 0 vs 1 on `mean_ms`, `p99_ms`, `p999_ms`, `over_50ms`, `gen0_collections` and
`alloc_bytes_per_frame`. Expected: no difference beyond run-to-run noise (compare the two "off" runs to see the noise).
SPSave's only per-frame code in Step A is one key check.

## Send back

`captures.csv`, `frameprobe\summary.csv`, the SPSave lines from `BepInEx\LogOutput.log`, and anything from A1 step 3
that didn't match.
