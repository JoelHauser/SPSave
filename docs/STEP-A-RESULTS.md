# Step A results (2026-11-06)

Build 0.1.0 on SPT 4.1.6 / EFT 0.16.9.5.40743, Joel's full mod list (SAIN, ORBIT, Realism, Forecaster and others).

## Capture

Every capture's round trip passed, and every raid with captures in it ended normally through `/client/match/local/end`.

| Map | Bots | World items | Containers | Doors | Save, warm | Save, first in session | On disk |
|---|---|---|---|---|---|---|---|
| Factory | 6 | 923 | 108 | 184 | 35 ms | 149 ms | 78 KB |
| Customs | 13 | 1,840 | 323 | 722 | 123–132 ms | 239 ms | 115 KB |
| Streets | 17–21 | 4,562 | 1,095 | 2,372 | 165–187 ms | 274 ms | 175 KB |

"Save" includes the read-back check (60–110 ms on Customs and Streets), which a real save won't run: a real save on
Streets is about 80–90 ms. The first save of a session pays one-time warm-up in the profile serializer (~60–80 ms).

About 30 ms of every outdoor save is writing the weather nodes as JSON (Customs 550 nodes, Streets 564): the "world"
step is 0.1–0.5 ms on Factory (no weather) and ~30 ms on both outdoor maps whatever the door count. Can move off
the main thread if it matters.

Weather: Factory has no WeatherController (Forecaster also skips it). Customs and Streets: nodes captured and match
the running curve. Clock: across 20 s, remaining time fell 20.0 s and game time rose 140 s (time factor 7).

Combat rule: in combat while a living bot had seen or shot at Joel within 30 s; cleared when that bot died
(Customs) or its contact aged out (Streets). Every hostile bot has an enemy entry for the player from spawn, so
"knows" alone means nothing.

Open: 4 (Customs) and 8 (Streets) loot entries skipped silently by the build used; the current build names them.

## Normal-play cost

One run without SPSave and one with it (Customs, naked PMC standing still, ~3–4 min recorded each, no captures).
Noise is the spread across 30-second segments within a run.

| Metric | Off | On | Spread of 30 s segments, off / on |
|---|---|---|---|
| Mean frame | 16.27 ms | 16.57 ms | 15.25–17.17 / 14.93–17.48 |
| Median | 15.55 ms | 15.96 ms | 14.87–17.00 / 14.57–17.07 |
| 95th percentile | 19.17 ms | 19.24 ms | 16.42–20.24 / 16.33–19.95 |
| 99th percentile | 26.22 ms | 28.33 ms | 22.57–26.55 / 22.12–30.65 |
| Frames over 50 ms per minute | 8.9 | 9.0 | 4–16 / 4–14 |
| Gen-0 collections per minute | 4.76 | 4.77 | 4–6 / 4–6 |
| Allocated per frame | 599 KB | 547 KB | 537–652 / 488–579 |

Everything but the 99th percentile sits inside the spread. Collections per minute are identical, so SPSave adds
no allocation pressure. The 99th percentile is 2 ms higher with SPSave because one of its segments reached 30.65 ms;
its other segments overlap the off run. Two runs cannot separate that from what was on screen. SPSave's only
per-frame code in Step A is one key check, which can't plausibly cost 2 ms. The direct way to settle it is to time
that Update itself (see Step B).

A 9.5 s frame on Streets (with captures) was not SPSave: no capture near it, ORBIT logged it with a full gen-0/1/2
collection in that frame and no time in ORBIT.
