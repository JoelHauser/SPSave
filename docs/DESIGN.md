# Save & Resume Raid: design

Target: SPT 4.1.6 on EFT 0.16.9.40743 (`H:\SPT4.1.X`). Solo SPT, PMC raids first.

## What players are promised

Your raid, your gear, the world and your progress come back exactly. The bots come back as the same bots in the same
places and carry on with their normal behaviour. A raid can be saved and resumed any number of times; extracting or
dying applies normal SPT progression once.

## Outcomes

| What happens | Result |
|---|---|
| Save & Exit, not in combat | Resume restores the raid exactly as below |
| Save & Exit in combat | Confirmation first; the raid resolves as **Killed** on next launch (loaded behind the loading screen and ended there) |
| Abandon (from the locked menu) | The raid resolves as **MIA** (loaded behind the loading screen and ended there) |
| Hard exit (Alt-F4, crash) in any raid | The raid is scrapped exactly like standard SPT: no penalty, profile stays pre-raid. A save used by Resume is gone. |

"In combat": a living bot sees you now, or saw or shot at you within 30 s (configurable). A bot that only knows
about you (heard you, was told) does not count. Checked once, at save time.

A save is used up when the resumed raid hands you control, as soon as the loading screen fades (no Ready prompt:
Joel, 2026-11-06). Until then, a failed resume returns to the menu with the save intact. The three newest used-up
saves are kept in `used\` for troubleshooting; older ones are deleted.

## What comes back

| Exact | Rebuilt | Lost |
|---|---|---|
| Inventory and item ids, chambered round, magazine contents, malfunctions, durability, found-in-raid flags | Bleeds, painkillers and other effect timers (re-added from saved timings) | Momentum; saving mid-air is refused |
| Health per body part, energy, hydration | Position, facing, pose, stamina | Bullets and grenades in flight |
| Looted containers, dropped items, corpses with pose and loot | Living bots' behaviour (they start fresh, unaware) | A bot's action in progress |
| Doors and switches | Future spawns (waves already fired are not repeated) | BTR, airdrop in flight, artillery, seasonal events (first version) |
| Raid time left, time of day, weather and its transition | | Sounds |
| Quest progress, XP, skills, kills, stats (applied once) | | |

## Lifecycle

**Save & Exit.** Freeze, capture (read-only), write and verify, then quit the game. `BaseLocalGame.GameEnd` never runs,
so the server gets no raid end: no extract, death or MIA.

**Between sessions.** The server profile is still pre-raid; all raid progress lives in the save. While a save
exists, the profile is locked (traders, flea, hideout transfers, mail, other raids) and the menu offers only
Resume or Abandon. Without the lock, kit could be sold and then resumed: duplication.

**Resume.** The normal raid start, with the saved profile, the saved world in place of fresh loot, the saved spawn
point, clock, time of day and weather, the opening waves suppressed and the living bots spawned from their saved
profiles. The world is held only while the loading screen fades, then you have control.

**End.** The normal `local/end` with the profile carried through every session, applied once.

## Why progress can't count twice

The server writes nothing during a raid. `LocationLifecycleService.EndLocalRaidAsync` takes everything (inventory,
skills, XP, quests, `TaskConditionCounters`, stats, health, trader standing, insurance losses) from the client's
profile at `/client/match/local/end`, and the client only sends that from `GameEnd`.

## Steps

- **A. Capture spike.** Done: STEP-A-RESULTS.md.
- **B1. Resume the player and the world, plus Save & Quit.** Done (passed in game 2026-11-06).
- **B2. Living bots** come back from their saved profiles in their saved places; later spawn waves continue, earlier
  ones don't repeat. Checked against SAIN, ABPS and ORBIT first. Done (2026-11-06).
- **B3. Repeat and finish:** a second save and resume of the same raid, then extraction applies progress exactly once,
  with a duplicate check. Done (2026-11-06; the extraction and duplicate check passed in game).
- **C. Server mod and lock.** The server locks the profile while a save waits; the menu is EFT's own reconnect mode
  (RECONNECT resumes, LEAVE abandons as MIA); Abandon and Killed end the raid behind a black screen. Done on Factory
  (2026-11-07).
- **D. Pause-menu SAVE & QUIT**, version and mod-list checks on resume. The Ready prompt was built (0.8.0–0.8.4) and
  dropped (0.8.5): the resume hands over control when loading ends. Hotkeys removed. Button, combat question and
  layout passed in game; 0.8.5 not yet. Then other maps, then release.
