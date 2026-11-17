# AAEmu.Geometry

Static geometry read from the ArcheAge client data, shared by the 3.5 server trees.

This is phase **P0** of `Docs/Reports/2026-11-15-zone-physics-implementation-plan.md`: the geometry layer
that used to live in `AAEmu.Game/Physics/Static` is now a project of its own, with no reference to
`AAEmu.Game` and none to Jitter2.

## The rule

The package turns client bytes into plain geometry and answers queries on it. It never creates physics
bodies, never touches a world, a unit or a tick. Turning this into colliders is the host tree's job.

| Must not reference | Why                                                                                     |
| ------------------ | --------------------------------------------------------------------------------------- |
| `AAEmu.Game`       | Otherwise it cannot be shared with the sibling 3.5 tree                                 |
| `Jitter2`          | Otherwise the package is welded to one physics engine; hosts map coordinates themselves |
| `NLog`             | Logging is a host concern — the host assigns `GeometryLog.Sink`                         |

Coordinates are the game's own: metres, Z-up, CryEngine-native. Vertices, triangles and boxes come out
exactly as the client data holds them.

## Layout

```text
Abstractions/
  IGeometryFileSource   — where client files come from (pak, unpacked folder, test fixture)
  StaticCollisionCell   — one cell to load, plus StaticGeometry constants
Source/                 — bytes → geometry
  CgfPhysicsReader      — .cgf/.cga: CryPhysics proxies (box, trimesh, sphere, cylinder, capsule)
  CgfRenderReader       — .cgf/.cga: the render mesh (A/B reference, and the fallback when a proxy is absent)
  BrushPlacementFile    — object.dat / visareas.dat / object_indoor.x2d: brush, voxel and water placement
  WaterVolumeRecord     — object type 11 record (contour, surface, depth)
Index/                  — geometry → queries
  CollisionMesh         — triangles, raycast, BVH build
  StaticCollision       — a zone's brushes instanced under a BVH: Raycast / TryFloor / AnyBoxOverlaps
  CollisionAssetCache   — process-wide asset → mesh (weak entries), read once per asset
  StaticCollisionOptions — the policy bits (underground floors, inland water, swim stance, …)
Water/
  ZoneWater             — inland water volumes: TrySurface, ocean level, swim stance rules
GeometryLog             — optional log sink the host wires to its own logger
```

## Using it from a host

```csharp
using AAEmu.Geometry;
using AAEmu.Geometry.Abstractions;

// 1. Adapt your file source once (pak, folder, …).
IGeometryFileSource files = new MyClientFiles();

// 2. Optionally forward the package's diagnostics to your logger.
GeometryLog.Sink = (level, message, exception) => myLogger.Log(level, message, exception);

// 3. Pick where collision geometry comes from (defaults to the proxy with a render fallback).
StaticCollision.Options = StaticCollisionOptions.Default with
{
    PhysicsSource = GeometryPhysicsSource.ProxyThenRender
};

// 4. Build a zone's geometry; it is read-only and can be shared between threads.
var collision = StaticCollision.Load(files, [
    new StaticCollisionCell("main_world", 10, 15, oceanLevel: 100f)
]);

// 5. Query it.
if (collision.TryFloor(x, y, above: z + 3f, below: z - 3f, out var floor))
    z = floor;
```

### Collision source

`GeometryPhysicsSource` decides what a brush collides with. Measured on zone 129 of the 3.5 client
(5 670 brushes, 549 assets):

| Mode | Asserts kept | Unique triangles | Mesh memory |
|---|---|---:|---:|
| `Proxy` (proxies only) | 549 of 593 — **44 assets dropped** | 156 727 | 6.05 MB |
| `ProxyThenRender` (default) | 593 of 593 | 174 342 | 6.84 MB |
| `Render` | 593 of 593 | 859 997 | 37.36 MB |

The native dedicate physicalises the proxy, not the render mesh, so `ProxyThenRender` is what matches the
game while keeping assets that ship no proxy (`ndeco_common_straw01`, `ndeco_backpack_l`, `grass_weed_a_brown`,
`waterweed_b` in cell `010_015` alone). `Render` exists for A/B and diagnostics;
`AAEMU_GEOMETRY_PHYSICS_SOURCE=proxy|render|proxy-then-render` overrides the mode.

See `Docs/Reports/2026-11-15-p1-proxy-vs-render.md` for the 30-asset table.

## Verification

`tools/GeometryProbe` runs this code against real client data (`--game <folder> | --pak <game_pak>`,
then `--zone`, `--cell`, `--cgf`, `--list`). With the 3.5 client `Live-3500_30.10.2026`, zone 129 gives
5 670 brushes of 549 assets, 2 158 704 placed triangles and 17 water volumes in ~690 ms — the same
numbers as before the move.
