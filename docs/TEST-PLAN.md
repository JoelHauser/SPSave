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
