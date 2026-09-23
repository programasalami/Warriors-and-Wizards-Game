# Warriors & Wizards

A RotMG-inspired action game: a desktop client, a browser client built from the same sources, and two servers.
Built on the [Zolmex/Alloy-Server](https://github.com/Zolmex/Alloy-Server) and
[NotTheLegend/Alloy-Client](https://github.com/NotTheLegend/Alloy-Client) foundations - credits to them and their contributors.

## What is in this repository

| Folder | What it is |
| --- | --- |
| `AlloyClient/` | The desktop client (C# / .NET 10, OpenTK, OpenGL 4.3). `WarriorsAndWizards.Client.sln` holds the engine, UI, audio, content builder and the game. |
| `WebClient/` | The browser client (WebAssembly + WebGL2). Built FROM the desktop sources by `tools/build_web.py`; nothing game-related is written twice. |
| `alloy-server/` | `AccountServer` (HTTP :8080, accounts / characters / rewards, Postgres + Redis) and `GameServer` (TCP :2050, the simulation). `WarriorsAndWizards.Server.sln`. |
| `Shared/` | `Common.Protocol`: what client and server must agree on (packet ids, level rules, positions). |
| `Launcher/` | The launcher players download (Windows + Linux): downloads the game into a `game` folder next to itself, keeps it up to date from `download/manifest.json`, starts it. `Tests/Launcher.Tests`. |
| `Portal/` | The Portal (`portal.<domain>`): the public player / guild / leaderboard / wiki site, RealmEye-style. Static pages in `site/`, data + icons built by `Tools/Portal/build_portal_data.py`, API = the account server's `/public/...` (`AccountServer/Systems/Public`, `Common/Database/PublicDb.cs`). `deploy -SetupPortal` once, `deploy -Portal` per update. The same pages exist inside the client (title screen PORTAL row, `AlloyClient/Screens/Components/Portal`). |
| `Tools/` | Developer tools (map editor, art-sheet and map generators). Not shipped. |
| `Docs/` | `EngineeringAudit.md`: the architecture, the audit findings, the change log and the manual test list. Read it before changing engine, network or persistence code. |
| `deploy.ps1` / `promote.ps1` | Release scripts (see the header of each). |

## Build and test (no game window opens)

```
dotnet build AlloyClient/WarriorsAndWizards.Client.sln          # the client; build the SOLUTION, not the single project
dotnet test  AlloyClient/WarriorsAndWizards.Client.sln
dotnet build alloy-server/WarriorsAndWizards.Server.sln     # both servers
dotnet test  alloy-server/WarriorsAndWizards.Server.sln
python WebClient/tools/build_web.py           # the browser client (needs the desktop client built first)
dotnet test  Launcher/Tests/Launcher.Tests/Launcher.Tests.csproj   # the launcher
```

.NET 10 SDK (pinned by `global.json` in each solution). Python 3 for the tools and the web build.

## Run locally

1. Install PostgreSQL 17 and Redis (Memurai on Windows). Create an empty database and put its settings in
   `alloy-server/Common/Resources/Config/Data/postgresConfig.xml` (copy the `.example.xml`; the real file is git-ignored). The
   servers create their own tables.
2. Start `alloy-server/bin/debug/net10.0/AccountServer.exe`, then `GameServer.exe` (each in its own window).
3. Start `AlloyClient/AlloyClient/bin/Debug/net10.0/AlloyClient.exe`. The source is configured for `127.0.0.1`; `deploy.ps1`
   swaps in the VPS address only in what it publishes.

In game: **F5** (or the DEV tab) shows the performance readout. The game server prints a `[STATS]` line every 10 seconds.
Logging: the client logs at Information (`ALLOY_LOG=debug` for more); the servers skip Debug lines unless `ALLOY_DEBUG_LOG` is set.

## Release

`deploy.ps1 -SetVersion x.y.z` sets the version in the client and the server config together (they must match: the game server
turns away any other client version), then `deploy.ps1 -Server -Client -Web`. `promote.ps1` mirrors the everyday Testing folder
into the git repository folder after building and testing everything. See `CLAUDE.md` for the folder roles.

## Requirements for players

Desktop: Windows, a GPU with OpenGL 4.3 (the shaders use shader storage and uniform buffers). Browser: WebGL2 and WebAssembly
(current Chrome, Edge, Firefox; Safari 15+).
