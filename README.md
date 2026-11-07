# SPSave: Save & Resume Raid

An SPT mod that lets you save a raid, close the game, and pick up the same raid later: your gear, the world and the
bots come back where you left them.

> **Status: in development, not released.** It is being tested in game, so far on Factory only. Don't use it on a
> profile you care about yet. There is no download; a release will come after testing on more maps.

Built for **SPT 4.1.6** (EFT 0.16.9.40743), solo PMC raids.

## How it works

- **Save.** In a raid, press Escape and choose **SAVE & QUIT**. The raid is saved and checked, and the game closes.
- **Resume.** Start the game. The main menu shows EFT's own reconnect screen. **RECONNECT** loads the raid as you
  left it, and you're back in control as soon as the loading screen fades.
- **Abandon.** On the same screen, **LEAVE** ends the raid as Missing in Action: you lose the gear you took in, as
  with a normal MIA.
- **Saving in combat** is allowed, but it counts as dying: you're asked first, and the raid ends as **Killed** the
  next time you start the game. The button is not a way out of a fight.
- **Hard exit** (Alt-F4, crash) is the same as without the mod: the raid is lost and your profile stays as it was
  before the raid.

While a save is waiting, your profile is locked (traders, flea, stash, hideout transfers, mail and other raids), so
gear can't be duplicated or lost between quitting and resuming. Extracting or dying at the end counts your XP, kills,
quests and stats once, across every session of the raid.

## What comes back

| Exactly | Rebuilt | Not restored |
|---|---|---|
| Your inventory, magazines, chambered rounds, durability, found-in-raid flags | Bleeds, painkillers and other effects with their timers | Bullets and grenades in flight |
| Health, energy, hydration | Your position, facing, stance and stamina | What a bot was doing at that moment |
| Looted containers, dropped items, bodies and their loot | Living bots, in their places with their gear (they start unaware of you) | BTR, airdrops, artillery, transits, seasonal events |
| Doors and switches | Later spawn waves (ones that already came don't repeat) | Sounds |
| Raid time left, time of day, weather | | |

## Compatibility

Tested with SAIN (bots keep their personality), ABPS and ORBIT. If your mods change between saving and resuming,
you're told which ones before the raid loads. A save from another game version can only be abandoned.

## Install (once released)

Two parts, both required: the client plugin `BepInEx/plugins/SPSave.dll` and the server mod
`user/mods/SPSave/SPSave.Server.dll` in your SPT server folder. Restart the server after installing. Saves are kept in
`SPSave\saves\` in your SPT folder; only the three most recent used-up saves are kept.

## For developers

Design: [docs/DESIGN.md](docs/DESIGN.md). Test plans: [docs/TEST-PLAN.md](docs/TEST-PLAN.md). Current work is on the
`c-server` branch.

```
dotnet build src\SPSave -c Release -p:SPTPath="<SPT root>"
dotnet build src\SPSave.Server -c Release -p:SPTPath="<SPT root>"
dotnet run -c Release --project tests\SPSave.Tests
```

The client build needs SAIN installed in that SPT folder (a compile-time reference; SAIN is optional when playing).
