# AAEmu.GeometryProbe

Offline probe of the static-geometry layer (`AAEmu.Game/Physics/Static`) against real client files.
It reads either an unpacked `game` folder or a `game_pak` (in place, through `AAPak`) and prints what
the readers found, so a claim can be checked against the shipped data without starting the server.

## Why it exists

The geometry layer (`CgfPhysicsReader`, `BrushPlacementFile`, `StaticCollision`, `ZoneWater`,
`StaticGeometryManager`, `NpcGroundResolver`) is normally exercised only while the game server runs.
That makes two kinds of mistake easy: a reader that silently returns nothing, and a report number
that nobody can reproduce. This probe closes both gaps — it is the same code path the server uses,
pointed at the client data and run from the command line.

## Usage

```powershell
# world/xml report + the first zones (default: the local unpacked game folder)
dotnet run --project tools/GeometryProbe

# the same against the archive
dotnet run --project tools/GeometryProbe -- --pak "e:\games\AAClassic\Live-3500_30.10.2026\game_pak"

# one zone: its cells and the static-geometry counters actually built from them
dotnet run --project tools/GeometryProbe -- --zone 129

# one .cgf: collision proxies as the reader sees them
dotnet run --project tools/GeometryProbe -- --cgf game/objects/box.cgf

# one cell's object.dat / visareas.dat, parsed directly, plus what one cell contributes
dotnet run --project tools/GeometryProbe -- --cell 010_015

# what the client actually contains under a folder
dotnet run --project tools/GeometryProbe -- --list game/worlds/main_world/cells/010_015
```

| Option                              | Meaning                                                                                 |
| ----------------------------------- | --------------------------------------------------------------------------------------- |
| `--game <dir>`                      | unpacked `game` folder (default: `e:\games\AAClassic\Live-3500_30.10.2026\game`)         |
| `--pak <path>`                      | `game_pak` to read instead of a folder                                                   |
| `--world <name>`                    | world to read, default `main_world`                                                      |
| `--zone <key>`                      | zone key from `world.xml`: prints its cells and builds `StaticCollision` over them       |
| `--cell <XXX_YYY>`                  | reads one cell's `object.dat` / `visareas.dat` and prints brush, asset and water counts  |
| `--cgf <path>`                      | probes one `.cgf`/`.cga` proxy (repeatable); skips the world report                      |
| `--list <dir>` / `--list-limit <n>` | lists files by folder, or by name prefix when the folder itself is not an entry           |

`--game` wins over `--pak` when both are given. Exit code is `0` on success, `1` when the source
cannot be opened, `2` when any `--cgf` asset is missing or has no proxy.

## What is worth knowing

-   The probe passes its own `ICollisionAssetSource` to `StaticCollision.Load`.
    `ClientFileCollisionAssetSource` works only after `ClientFileManager.Initialize()` has run, which
    a console host does not do — without this the counters come out as zeros.
-   `AAPak.GetFilesInDirectory` matches by exact directory, and a folder is usually not an entry in
    the pak — a real folder therefore reports `0 files`. `--list` falls back to a name-prefix search,
    which is the view that matches reality (`010_015` holds 15 files).
-   `BrushPlacementFile.ReadWater` only applies to the outdoor object file. Called on `visareas.dat`
    it throws — the probe therefore reports `visareas.dat` water as empty.
-   Numbers seen with the 3.5 client (`Live-3500_30.10.2026`):
    -   `main_world/world.xml` — 147 zones, 35×38 cells, ocean level 100;
    -   zone 129 — 5 670 brushes of 549 assets, 2 158 704 placed triangles, 17 water volumes, ~870 ms;
    -   zone 246 — 6 057 brushes of 325 assets, 4 990 522 placed triangles, 18 water volumes, ~580 ms;
    -   `object.dat` of `010_015` — 1 574 brushes of 196 assets, 3 water volumes;
    -   `visareas.dat` of `010_015` — 117 brushes (the indoor objects `object.dat` leaves out).
-   `main_world` cells ship no `world_server` folder, so `StaticCollision` takes the outdoor brushes
    from `client/object.dat` and the indoor ones from `client/visareas.dat`
    (`AAEMU_ZONE_UNDERGROUND_FLOORS=0` turns the latter off: zone 129 goes from 5 670 brushes / 549
    assets to 5 457 / 475, so `visareas.dat` currently contributes ~213 brushes to that zone).
