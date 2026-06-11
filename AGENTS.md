# AGENTS.md

## Project

HBT (Hearthstone Battleground Tracker) — WPF plugin that reads Hearthstone memory via UnitySpy, tracks Battlegrounds league matches, and reports results to a league API.

## Build

```powershell
dotnet build -c Release
```

Targets **.NET Framework 4.72**, x86 only. The README incorrectly says .NET 8 — trust the csproj.

Output: `bin\Release\net472-windows\HearthstoneBattlegroundTracker.exe`

## Testing

No standard test framework. `BattlegroundSpy.Test` is a console app that reads live Hearthstone memory:

```powershell
dotnet run --project BattlegroundSpy.Test -c Release
```

Requires Hearthstone to be running. Cannot run in CI or headless environments.

## Architecture

- **Main app** (`HearthstoneBattlegroundTracker.csproj`) — WPF, references BattlegroundSpy + Plugins
- **BattlegroundSpy** (`BattlegroundSpy/`) — Memory reader via UnitySpy. Core file: `BattlegroundSpyReader.cs`
- **UnitySpy** — Linked from `../../HDT_Reverse/unity-spy/` (source compiled into BattlegroundSpy.dll, not vendored in this repo)
- **Plugins/** — `HdtCompat` and `BobsBuddyCompat` are stub/compatibility assemblies
- **Services/** — `GameMonitorService` (state machine), `HearthMirrorService` (BGSpy wrapper), `LeagueClient`, `ApiClient`
- **Parser/** — Power.log parser (secondary data source)

## Key Facts

- Windows-only (WPF + x86)
- Version lives in both `HearthstoneBattlegroundTracker.csproj` and `BattlegroundSpy/BattlegroundSpy.csproj` — update both when bumping
- `DefaultItemExcludes` in main csproj excludes `BattlegroundSpy\**` and `BattlegroundSpy.Test\**` — they build independently
- UnitySpy source path is relative (`../../HDT_Reverse/unity-spy/`) — won't resolve outside the original dev machine layout
- No CI workflows, no linting, no formatter configured
- `NoWarn`: CS8632 (nullable annotation), CS0649 (uninitialized field)
