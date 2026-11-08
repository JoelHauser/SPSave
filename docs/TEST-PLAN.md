# B3: the same raid saved twice, then extracted (0.5.0)

About 12 minutes, with one extraction. Progress from every session must count once at the end.

**First raid, Factory (day), naked PMC:**

1. Earn some XP: search a container or two, and kill a scav if one comes (shoot from cover). Note the kill count.
2. Out of contact, **Ctrl+F10**.

**Relaunch, start Factory, Ctrl+F11.**

3. Earn a little more: search another container or kill another scav.
4. Out of contact, **Ctrl+F10** again (second save of the same raid).

**Relaunch, start Factory, Ctrl+F11.**

5. **Extract** (Gate 3 or Gate 0; check the exits with the double-tap of O).
6. On the results screen, the kills and XP include all three sessions.

**Send back** the `SPSave` lines from `BepInEx\LogOutput.log` of each launch (the file is rewritten every launch, so
copy it after each one), plus the results screen numbers. The lines that matter:
- At each save: `Captured ... no duplicate items ...`.
- At each resume: `Raid statistics carried on from the save: N session XP so far, K kills` (growing each time) and
  `One-raid quest counters carried on from the save`.
- Optional: if a save lands a minute before 8:20 or 14:20, `Wave timers aligned to the raid clock` and the PMC wave
  at about 498 s or 858 s (checks 0.4.2).

# B2 step 3: spawning carries on after a resume (0.4.2)

0.4.1 passed 2026-11-06: saved at 7:03, the 8:20 PMC waves fired 77 s after resume, at raid time 491/496 s (about
7 s early: resume loading). 0.4.2 aligns the wave timers to the raid clock; expect ~498 s and a line
`Wave timers aligned to the raid clock (loading time): N boss wave timers +X s`.

0.4.0 run 2026-11-06: the saved boss waves did not read back (game JSON fails on a null PerfectPos), so the resume
spawned no PMC waves; bots and personalities were fine. 0.4.1 saves waves in state.json itself.

About 10 minutes. ABPS's first PMC wave here comes 500 s into the raid (`delayBeforeFirstWave`), so the save goes
just before it and the resume should bring that wave about a minute later, not 500 s later.

**First raid, Factory (day), naked PMC:**

1. Stay hidden until about **13:30 into the raid** (Factory is 50 min here: the timer shows about 36:30 left). Out
   of contact. On Factory ABPS makes only two PMC waves, at 500 s and 860 s (8:20 and 14:20): it stops 1500 s before
   the end. Save before 14:20 or there is nothing left to come (0.4.1 run at 19:10: 9/9 waves had fired, correctly).
2. Press **Ctrl+F10**.

**Relaunch, start Factory (day), Ctrl+F11 when it loads, then stay put for about 2 minutes. Leave.**

**Send back** every `SPSave` line from `BepInEx\LogOutput.log`. At save time:
- `Save check: N boss waves (F fired, T s in) and ... read back`.

On resume:
- `Resuming the raid ... N boss and N old-style waves saved` and `Waves restored: K/N boss waves to come (next X s: ...)`:
  the starting PMCs and anything already fired are gone; the next PMC wave is about (860 − 810) s away.
- `Resumed raid: boss wave pmcUSEC/pmcBEAR spawning at raid time ~860 s`: the wave fired on the raid's clock.
- No `Holding fresh bot spawns` line. Restored bots still come back as in step 2.

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

> Historical (done 2026-11-06). The FrameProbe tool, the `captures.csv` log and the Ctrl+F10 hotkey used here were removed
> by 1.0.0; they are in the git history. The "Captured …" line in the BepInEx log still has the save timings.

Step A captures a raid to disk and reads it back. It restores nothing and never ends the raid, so a raid with
captures in it must play and extract exactly like one without.

## Setup

```
dotnet build src\SPSave -c Release -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true
dotnet build tools\FrameProbe -c Release -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true
```

Capture key: **Ctrl+F10** (BepInEx config `com.mybutthasarash.spsave.cfg`). Output goes to `H:\SPT4.1.X\SPSave\`:
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

## C2. Ending a save as MIA or Killed (0.6.1)

Short runs on Factory. Each needs a fresh save. Before each save, earn a little XP so it shows: kill one scav or
search one container. Note your level XP on the character screen before the raid.

1. **MIA.** Make a save (Ctrl+F10). Restart the game. In the F12 menu, under "Step C (testing)", set "End the next
   resume as" to MissingInAction. Start a PMC raid on Factory. Expected: the raid loads, then ends at once as Missing
   in Action, with the screen black throughout: the raid world must never show. The results screen shows MIA and the
   XP from the saved session. The setting goes back to Resume.
2. **Killed.** Same, with Killed. Expected: the raid loads and, still on black, the results screen shows
   Killed. Your gear is gone except the secure container.
3. After each: the save is gone (moved under `SPSave\saves\<profile>\used\`). Starting another Factory raid starts a
   fresh one. On the character screen, XP went up by the saved session's amount, and the stats count one more MIA or
   death.

Send back: the SPSave lines from `BepInEx\LogOutput.log` ("This load ends the saved raid as ...", "Save used up",
"Ending the saved raid ..."), and anything on screen that looked off.

## C3. The locked menu (0.7.0)

Install both DLLs from the zip (client plugin and `SPT_Runtime\user\mods\SPSave\SPSave.Server.dll`), then restart
the **server** too. The server console should show `[SPSave] Loaded: 8 lock patches.` Short Factory runs.

1. **Lock.** Make a save (Ctrl+F10). The game closes. The server console shows `[SPSave] Profile ... locked`.
2. **Locked menu.** Start the game. Expected: the main menu in EFT's reconnect mode. The big button reads RECONNECT,
   Trade, Hideout and Character are hidden, and the bottom bar is greyed out. Nothing on screen should look like it
   came from a mod.
3. **Leave, then cancel.** RECONNECT opens the reconnect screen (map, level, character). Press LEAVE, then answer
   No in the confirmation. Expected: back on the reconnect screen, its buttons working.
4. **Resume.** Press RECONNECT on that screen. Expected: the Factory raid loads without the map screens and resumes
   as before (Ctrl+F11 to take control). The server console shows `[SPSave] Profile ... unlocked`. Escape, then
   Leave, as usual.
5. **Abandon.** Make another save, restart, RECONNECT, LEAVE, Yes. Expected: black screen, then MIA results, as in
   C2. After that, the normal main menu is back.

Send back: the SPSave lines from `BepInEx\LogOutput.log`, the `[SPSave]` lines from the server console or
`SPT_Runtime\user\logs\spt\`, and anything that looked off, especially anything that didn't look like stock EFT.

## P1. Pit FireTeam squad (0.9.0 client, 0.8.0 server)

Restart the **server** (new server mod). Factory, one saved teammate in your group (My Squad → Invite, or Auto Join).

1. Start the raid. SPSave's log at startup: `Pit FireTeam 1.1.1 found`. Server console: `Pit FireTeam found: 9
   teammate actions locked`.
2. Out of combat, Escape → SAVE & QUIT. Log: `Pit FireTeam: 1 squadmate: <name> (squadmate)`.
3. Before resuming, open My Squad and try to change that teammate's loadout: refused (server: `Refused Pit FireTeam
   SetLoadout`).
4. RECONNECT. Expected: **one** copy of your teammate next to you, following you and answering commands, with the gear
   and health it had. Log: `Pit FireTeam squad for this resume: carried back <name>`.
5. Extract together. Pit FireTeam's own raid report shows the teammate once; no item turns up twice.
6. Optional, second run: save with the teammate, then LEAVE → Yes. Log: `1 squadmate(s) reported lost`. With the F12
   setting on `ComeHomeSafe` instead: `squadmates come home safe`.
7. Optional: recruit a friendly bot (Allegiance), save, resume: `recruit back as your follower`.

## D2. Straight back in, no hotkeys, old saves pruned (0.8.5)

Ctrl+F10 and Ctrl+F11 are gone from 0.8.5 on (sections above are history): save from the pause menu's SAVE & QUIT;
a resume needs no key. One short Factory run, server unchanged:

1. Escape, SAVE & QUIT (out of combat). The game closes.
2. Start, RECONNECT. Expected: control from the moment the loading screen fades, as in a normal raid start: no
   window, arms in the normal pose for what you hold, no pause after the fade, facing the way you were (0.8.6).
3. `H:\SPT4.1.X\SPSave\saves\<profile>\used\` holds 3 folders afterwards (was 37+).
4. Optional: the combat save's reconnect screen shows the "Attention" message and only LEAVE (0.8.2, not yet seen).

## D1. Pause menu, Ready prompt, mod check (0.8.0)

Short Factory runs. Server mod unchanged (0.7.1).

1. **SAVE & QUIT.** In a raid, press Escape. Expected: a SAVE & QUIT button above LEAVE, looking exactly like LEAVE.
   Not in combat: press it; the game saves and closes as Ctrl+F10 did.
2. **Ready prompt.** Resume that save. Expected: after loading, EFT's own message window: "Your raid is restored and
   paused. Continue when you're ready." Click its button: you take control. Check the mouse cursor shows and the
   button works with time stopped. If the window doesn't work, Ctrl+F11 still does.
3. **No LEAVE while paused.** Before taking control, press Escape. Expected: no LEAVE and no SAVE & QUIT.
4. **Combat save.** Let a scav shoot at you, Escape, SAVE & QUIT. Expected: a confirmation that the raid will end as
   Killed. Yes saves and closes. On the next start the reconnect screen shows only LEAVE; LEAVE ends it as Killed.
5. **Mod check.** Make a save, then disable one small client mod (or change nothing and skip this). On RECONNECT
   expect a confirmation naming the mod.
