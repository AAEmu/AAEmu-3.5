# AAEmu — Agent Guide

This file is written for AI coding agents that need to work on the AAEmu codebase. AAEmu is an open-source server emulator for the MMORPG ArcheAge, written in C# on .NET.

## Project overview

AAEmu implements the game server and login server needed to run a private ArcheAge server. The repository contains:

- **`AAEmu.Game`** — the main game server executable. Handles world simulation, characters, NPCs, items, quests, networking with game clients, and an embedded web API.
- **`AAEmu.Login`** — the login server executable. Handles client authentication, game-server registration, and exposes HTTP health endpoints.
- **`AAEmu.Commons`** — shared library used by both servers (networking primitives, database helpers, I/O utilities, JSON/XML parsing, cryptography).
- **`AAEmu.Aspire.AppHost`** — .NET Aspire orchestrator for local development. Spins up MySQL, the login server, and the game server with auto-wired connection strings.
- **Test projects**
  - `AAEmu.UnitTests` — unit tests using TUnit and Microsoft.Testing.Platform.
  - `AAEmu.IntegrationTests` — integration tests for the game server using xUnit v3 on the Microsoft Testing Platform.
  - `AAEmu.Login.IntegrationTests` — integration tests for the login server using xUnit v3, Moq, and Testcontainers for MySQL.

The project is licensed under the GNU Lesser General Public License v3 (or later). See `LICENSE` / `LICENSE.GPL`.

## Technology stack

- **Language:** C# with .NET 10 (`net10.0`).
- **SDK version:** pinned in `global.json` to `10.0.0` with `rollForward: latestMajor`.
- **Project system:** SDK-style `.csproj` files, centralized package management via `Directory.Packages.props`.
- **Networking:** custom TCP protocol built on `NetCoreServer`; login server also uses ASP.NET Core Kestrel with a custom `ConnectionHandler`.
- **Databases:**
  - MySQL 8.0 for account, character, and world persistence (`MySql.Data`).
  - SQLite (`Microsoft.Data.Sqlite`) for the bundled `AAEmu.Game/Data/compact.sqlite3` read-only game-data cache.
- **Scripting / commands:** C# scripting via `Microsoft.CodeAnalysis.CSharp.Scripting`; Lua via `NLua`; in-game GM commands live under `AAEmu.Game/Scripts/Commands`.
- **Physics:** `Jitter2` for collision/physics.
- **Logging:** NLog (`NLog.config` next to each executable).
- **Dependency injection:** `Microsoft.Extensions.DependencyInjection` / `Microsoft.Extensions.Hosting`.
- **Observability (login server):** OpenTelemetry OTLP export when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- **Container orchestration:** Docker Compose (`docker-compose.yaml`) and .NET Aspire (`AAEmu.Aspire.AppHost`).

## Repository layout

```
.
├── AAEmu.Game/                 # Game server executable and logic
│   ├── Core/Managers/          # Singleton manager classes (world, units, schedules, ...)
│   ├── Core/Network/           # TCP connections and packet routing
│   ├── Core/Packets/           # Packet definitions (C2G, G2C, L2G, G2L, proxy, debug)
│   ├── Data/                   # Static data: compact.sqlite3, paths, portals, world configs
│   ├── GameData/               # Game-data loading framework
│   ├── Models/                 # Domain models (units, items, skills, quests, housing, ...)
│   ├── Scripts/                # GM commands and script hooks
│   ├── Services/               # Web API, Discord bot, game service host
│   └── Configurations/         # JSON config files copied to output
├── AAEmu.Login/                # Login server executable
│   ├── Core/                   # Authentication, controllers, network handlers
│   ├── Models/                 # Login domain models and configuration
│   └── Properties/             # Resources
├── AAEmu.Commons/              # Shared utilities
├── AAEmu.Aspire.AppHost/       # Aspire local-dev orchestrator
├── AAEmu.UnitTests/            # TUnit unit tests
├── AAEmu.IntegrationTests/     # xUnit v3 game-server integration tests
├── AAEmu.Login.IntegrationTests/ # xUnit v3 login-server integration tests
├── SQL/                        # Base schemas and incremental update scripts
├── Scripts/                    # Build/run helper scripts (.bat, .sh, .ps1)
├── Docs/                       # Markdown documentation (English + Russian)
└── Directory.Build.props       # Shared MSBuild properties
```

### Key configuration files

| File | Purpose |
|------|---------|
| `global.json` | .NET SDK version and test runner (Microsoft.Testing.Platform) |
| `Directory.Build.props` | Shared MSBuild properties: company, copyright, target framework `net10.0`, version prefix/suffix, implicit usings |
| `Directory.Packages.props` | Central package version management |
| `nuget.config` | NuGet source configuration (nuget.org) |
| `AAEmu.slnx` | Modern XML solution file |
| `.editorconfig` | Code style, naming, and analyzer severity rules |
| `docker-compose.yaml` | Multi-service Docker setup for MySQL, Adminer, login, and game servers |
| `.env.example` | Example environment variables for Docker Compose |

## Build and run commands

### Prerequisites

- .NET 10 SDK.
- MySQL 8.0 server with the schemas imported from `SQL/aaemu_game.sql` and `SQL/aaemu_login.sql`.
- (Optional) ArcheAge client data mounted/copied for the game server (`ClientData`, `Data`, `Configurations`).

### Build the solution

```bash
dotnet build
```

### Run the servers locally

```bash
# Login server
cd AAEmu.Login
dotnet run

# Game server (in a separate terminal)
cd AAEmu.Game
dotnet run
```

Helper scripts are also provided in `Scripts/` (`start_game.sh`, `start_login.sh`, `start AAEmu.Game.bat`, etc.).

### Publish / container deployment

```bash
# Publish a project
dotnet publish ./AAEmu.Game/AAEmu.Game.csproj -c Release -o ./publish/game
dotnet publish ./AAEmu.Login/AAEmu.Login.csproj -c Release -o ./publish/login

# Docker Compose (copy .env.example to .env and adjust values first)
docker compose --env-file .env up --build
```

### Aspire local orchestration

Run the `AAEmu.Aspire.AppHost` project. It will provision a persistent MySQL container, create the `aaemu_login` and `aaemu_game` databases using the bundled SQL scripts, and start both servers with environment variables wired automatically.

## Configuration

Both servers use JSON configuration files layered in this order (later sources override earlier ones):

1. `Config.json` in the executable directory.
2. For the game server: all `*.json` files under `Configurations/` (sorted alphabetically).
3. `Config.Local.json` in the executable directory.
4. User secrets (`dotnet user-secrets`).
5. Environment variables.
6. Command-line arguments.

### Important files

- `AAEmu.Game/Config.json` and `AAEmu.Game/ExampleConfig.Local.json`.
- `AAEmu.Login/Config.json` and `AAEmu.Login/ExampleConfig.Local.json`.

`Config.Local.json` and `.env` are ignored by `.gitignore` and are the right place for secrets. The preferred way to store credentials is `dotnet user-secrets` inside the project directory.

### Game server key settings

- `Network` — public game-client TCP listener (default port `1239`).
- `StreamNetwork` — secondary TCP listener (default port `1250`).
- `WebApiNetwork` — HTTP web API listener (default port `1280`).
- `LoginNetwork` — how the game server reaches the login server (default `127.0.0.1:1234`).
- `Connections:MySQLProvider` — MySQL connection for the `aaemu_game` database.
- `ClientData` — list of ArcheAge client data sources (used in `Config.Local.json`).

### Login server key settings

- `InternalNetwork` — TCP listener for game-server registration (default port `1234`).
- `Network` — public client TCP listener via Kestrel (default port `1237`).
- `Connections:MySQLProvider` — MySQL connection for the `aaemu_login` database.
- `GameServers` — static list of registered game servers when not using the Aspire/Docker dynamic setup.
- `AutoAccount` — if `true`, unknown accounts are created automatically on first login.

## Code organization

### Game server (`AAEmu.Game`)

- **`Core/Managers`** — stateful singleton managers registered in DI:
  - `Core/Managers` — general systems (AI, animation, auction, chat, commands, crafting, experience, items, skills, ...).
  - `Core/Managers/UnitManagers` — character, doodad, and NPC managers.
  - `Core/Managers/World` — world, zone, faction, specialty, stream, area triggers.
  - `Core/Managers/Id` — global ID generators (auction, character, doodad, item, quest, team, etc.).
  - `Core/Managers/Stream` — UCC/picture streaming.
- **`Core/Network`** — low-level TCP session management and internal login-to-game connections.
- **`Core/Packets`** — packet classes grouped by direction:
  - `C2G` — client to game server.
  - `G2C` — game server to client.
  - `L2G` / `G2L` — login/game internal protocol.
  - `S2C` / `C2S` / `Proxy` / `Debug` — additional protocol channels.
- **`Models/Game`** — domain models for every gameplay system (units, items, skills, quests, housing, ships, slaves, doodads, schedules, etc.).
- **`Models/Tasks`** — scheduled background tasks grouped by system (NPCs, doodads, housing, item decay, etc.).
- **`Services`** — hosted services: `GameService` (main loop / boot), `WebApiService`, `DiscordBotService`.
- **`Scripts/Commands`** — GM/admin slash commands loaded at runtime.
- **`GameData/Framework`** — framework for loading XML/JSON/SQLite game data.

### Login server (`AAEmu.Login`)

- **`Core/Authentication`** — password and Korea-region auth methods.
- **`Core/Controllers`** — game controller, login controller, request controller.
- **`Core/Network`** — internal (game-server) and public (client) network layers.
- **`Core/PacketHandlers`** — packet dispatch for internal and public protocols.
- **`Core/Services`** — login service host and MySQL initializer.
- **`Models`** — configuration classes and domain models.

### Shared library (`AAEmu.Commons`)

- `Network/` — `NetCoreServer`-based base classes for TCP clients and servers.
- `Utils/DB/` — MySQL and SQLite helpers, including `MySQL.SetConfiguration(...)` used by the game server at boot.
- `IO/`, `Cryptography/`, `Models/`, `Exceptions/` — general-purpose utilities.

## Dependency injection conventions

- Both servers register services through `Microsoft.Extensions.DependencyInjection`.
- Most managers are registered as singletons exposing both the concrete type and an interface (`IManager` / `Manager`).
- Circular dependencies are broken by injecting `Lazy<T>` of the dependent service; a comment in `AAEmu.Game/Program.cs` documents the known cycles (WorldManager ↔ ZoneManager/IndunManager/FamilyManager, NameManager ↔ CharacterManager, HousingManager ↔ MailManager).
- `AAEmu.Game` exposes the `IServiceCollection` itself as a singleton so `ManagerOrchestrator` can inspect registrations.

## Code style guidelines

The project uses `.editorconfig` for style enforcement. Key rules:

- **Indentation:** 4 spaces for `.cs`; 2 spaces for `.csproj`, `.props`, `.config`, `.json`.
- **Line endings:** `crlf` for C#/VB; `lf` for shell scripts.
- **Encoding:** `utf-8-bom` for code and project files.
- **Language features:**
  - Implicit usings enabled.
  - Prefer `var`.
  - Prefer collection expressions and object initializers.
  - File-scoped namespaces are enforced (`IDE0161`).
  - Namespace must match folder structure (`IDE0130`).
- **Naming:**
  - PascalCase for types, methods, properties, non-private static fields, constants, local functions.
  - Private instance fields: `_camelCase`.
  - Private static fields: `s_camelCase`.
  - Locals and parameters: `camelCase`.
- **Analyzer severity:** many style and code-quality diagnostics are elevated to warning. Notable suppressed rules include `CA1031` (catching general exceptions), `CA1822` (marking members static), `CA2211` (visible non-constant fields), and `IDE0060` (unused parameters).
- **Spelling:** configured for `en-us,ru`; the exclusion dictionary is `exclusion.dic`.

When editing code, keep the existing style. Run `dotnet build` locally to catch analyzer warnings.

## Testing instructions

All test projects use the **Microsoft Testing Platform** (`global.json` sets `"runner": "Microsoft.Testing.Platform"`).

### Run all tests

```bash
dotnet test
```

### Run a specific project or test class

```bash
dotnet test ./AAEmu.UnitTests
dotnet test --filter "FullyQualifiedName~GameNetworkTests"
```

### Test projects at a glance

| Project | Framework | Notable packages | Notes |
|---------|-----------|------------------|-------|
| `AAEmu.UnitTests` | TUnit | `TUnit`, `TUnit.Mocks`, `Microsoft.Testing.Extensions.CodeCoverage` | Fast unit tests; uses `Microsoft.Extensions.TimeProvider.Testing` for time abstraction. |
| `AAEmu.IntegrationTests` | xUnit v3 (MTP bridge) | `xunit.v3.mtp-v2`, `Moq` | Needs a `Config.json` copied from `ExampleConfig.json` and a valid SQLite database in the output `Data/` folder. |
| `AAEmu.Login.IntegrationTests` | xUnit v3 (MTP bridge) | `xunit.v3.mtp-v2`, `Moq`, `Testcontainers.MySql` | Spins up MySQL in a container automatically. |

### Integration-test setup

For `AAEmu.IntegrationTests`:

1. Copy `AAEmu.IntegrationTests/ExampleConfig.json` to `AAEmu.IntegrationTests/Config.json`.
2. Replace the `%wildcards%` with the same values used for `AAEmu.Game`.
3. Copy the SQLite database (`compact.sqlite3`) from `AAEmu.Game/Data` into the compiled test `Data/` folder.
4. Rebuild and run `dotnet test`.

## Database migrations and schema

- Base schemas live in `SQL/aaemu_game.sql` and `SQL/aaemu_login.sql`. They are idempotent (`CREATE TABLE IF NOT EXISTS`, etc.).
- Incremental update scripts are in `SQL/updates/`.
- The login server runs `MySqlInitializer` to apply updates automatically when `Connections:AutoApplyUpdates` is `true`.
- The game server expects the MySQL database to be ready at startup; it does not automatically migrate schema by default.

## Deployment processes

### Docker Compose

The provided `docker-compose.yaml` builds and runs:

1. `db` — MySQL 8.0.36, initialized with `SQL/aaemu_login.sql` and `SQL/aaemu_game.sql`.
2. `adminer` — Database admin UI on port `8080`.
3. `login` — Login server image.
4. `game` — Game server image (depends on `login`).

Required environment variables are shown in `.env.example`:

```env
PROJECT_VERSION_PREFIX=0.3.0.0
PROJECT_VERSION_SUFFIX=alpha
BUILD_CONFIGURATION=Debug
BUILD_FRAMEWORK=net10.0
BUILD_RUNTIME=linux-musl-x64
DB_USER=root
DB_PASSWORD=password
```

Runtime data is mounted from `./.server_files/`.

### Aspire

Useful for iterative local development. The AppHost:

- Provisions a persistent MySQL 8.0 container with named databases.
- Applies `SQL/aaemu_login.sql` and `SQL/aaemu_game.sql` as creation scripts.
- Exposes the game server on ports `1239`/`1250` and the login server on ports `1234`/`1237`.
- Wires OTLP telemetry export for Aspire dashboard integration.

### Manual / self-contained publish

`Scripts/publish.sh` demonstrates cross-runtime self-contained publishing, but the runtime list is outdated; prefer `dotnet publish` with the runtime identifier you actually need.

## Security considerations

- **Never commit credentials.** `Config.Local.json`, `.env`, and user secrets are ignored by Git.
- The default `SecretKey` in `Config.json` is `"test"`; change it for any real deployment.
- Passwords in the login database are stored hashed; Korea-region auth uses a separate challenge-response hash.
- The game server compiles and runs C# scripts from `AAEmu.Game/Scripts/Commands` at runtime; ensure the server binary only loads trusted files.
- OpenTelemetry export sends telemetry to the configured OTLP endpoint only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## Useful conventions for agents

- Search for packet handlers in `AAEmu.Game/Core/Packets/` and `AAEmu.Login/Core/PacketHandlers/`.
- Game data tables from the client are usually loaded via the `GameData/Framework` loaders and cached in `compact.sqlite3`.
- Managers that need a guaranteed initialization order are orchestrated through `ManagerOrchestrator`.
- Use `NLog` loggers (`LogManager.GetCurrentClassLogger()`); avoid `Console.WriteLine` in production code.
- When adding a new manager, register it as a singleton in `Program.cs` and follow the existing `IManager` + `Manager` pair pattern.
- Keep documentation in `Docs/`; the wiki folder is published to the project wiki.

## Getting help

- Human documentation: `Docs/wiki/` and `README.md`.
- Setup details: `AAEmu.Game/README.md` and `AAEmu.Login/README.md`.
- Community: Discord and GitHub links in the main `README.md`.
