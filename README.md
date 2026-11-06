# SPSave: Save & Resume Raid

An SPT 4.1 mod for saving a raid, closing the game and the server, and resuming the same raid later.

**Status: Step A, a capture spike for testing.** It saves a raid to disk on Ctrl+F10 and checks the file reads back.
It does not resume raids yet. Don't install it to play.

- What it will do, and what comes back exactly: [docs/DESIGN.md](docs/DESIGN.md)
- Testing Step A: [docs/TEST-PLAN.md](docs/TEST-PLAN.md)

Build: `dotnet build src\SPSave -c Release -p:SPTPath="<SPT root>"`. Tests: `dotnet run -c Release --project tests\SPSave.Tests`.
