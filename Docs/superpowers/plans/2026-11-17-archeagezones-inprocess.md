# ArcheageZones in-process migration

> **For agentic workers:** Execute this plan task-by-task using superpowers:executing-plans. Independent geometry, ground and hull work is delegated under superpowers:dispatching-parallel-agents.

**Goal:** Remove Jitter2 and the old physics, port ArcheageZones simulation directly into the AAC3500 server.

**Architecture:** Keep 3.5 client serialization and gameplay models. Port domain algorithms (geometry, ground/water, hull bodies, contacts and fixed steps) with direct calls from WorldInstance. No zone process, zone TCP, ZW/WZ messages or 10.x wire types. Hull physics remains behind an internal IHullPhysics contract.

**Tech Stack:** .NET 10, C# 14, System.Numerics, TUnit.

**Spec:** User's explicit goal and follow-up in this chat; upstream ArcheageZones at db08214. Existing reports describe the earlier Jitter2 bridge and are superseded where incompatible with this goal.

## Constraints

- No LINQ or async/await in simulation hot paths; thread-safe long-running server.
- Build only with explicit `-c Debug` or `-c Release`.
- Preserve current client protocol and unrelated uncommitted changes.
- Native CryPhysics bindings for the upstream 10.x binary must never be assumed compatible with the 3.5 DLL.
- No commits or deployment requested.

## Tasks and evidence

- [ ] Geometry cache: regression tests for bounded LRU, true eviction, invalidation while building, build concurrency, and XY selection/placement identity; RED then GREEN.
- [ ] Ground/water: test known shallow bed -> Settle and high terrain decoding consistency; remove obsolete Jitter heightmap mirror and bounding box.
- [ ] Hull domain: port HullBody/HullModel/IHullPhysics/FittedHullPhysics/HullContacts/HullPose without wire dependencies, including steering, impulses, hull pairs, world isolation, dead rest and contact windows. Verify against upstream fixtures and focused tests.
- [ ] World adapter: replace PhysicsManager and ShipController/Jitter body references with direct IHullPhysics. Serialize mutation through one simulation thread, bound time catch-up, join thread on disposal; adapt spawn/control/recoil/explosion, transform and 3.5 movement.
- [ ] Remove legacy Jitter2 sources, package references and obsolete commented-out tests; prove no compiled Jitter dependency remains.
- [ ] Continue missing managed collision sources (doodads/houses/vegetation), cross-zone query coverage, collision damage and native-equivalent controller input guards as supported by real 3.5 data.
- [ ] Run Debug build, focused and full unit tests; source review and real-client geometry probe when available. Record exact remaining gaps and avoid claiming native fidelity without gameplay/traces.

## Review focus

World/instance isolation; removal before first tick; stale zone completion after invalidation; 2 m terrain sampling and cell coefficients; sbyte helm -128, NaN/Infinity and long clock stalls; packet velocity saturation and authoritative transform; collision damage cadence; attached riders and objects.

## Rulings

- Existing mixed working tree is the requested target: reuse it, do not create a divergent checkout.
- Old Jitter-specific defects (half-size boxes, stale static rigid bodies and terrain mirror scaling) are eliminated by removing those paths; their observable failure modes require tests at the new simulation boundary.
- Port managed hull simulation first; upstream native backend is ABI-specific and cannot be loaded into this long-running process until its binary contract is verified for the requested build.

## 2026-11-17 progress and remaining work

- Game and test projects build in Debug without a Jitter2 source/package reference. The latest full suite ran 1210 tests: 1207 passed; two `BuffTests` type assertions and one login test failed outside this migration. Hull tests passed after finite-input guards and water/terrain changes.
- Doodad collision snapshots are built asynchronously from 3.5 client models. The live `game_pak` resolves valid proxy meshes for 7/8 ship-colliding template models. Template 8611 uses an animated `.chr` with no default proxy; its actual render mesh has 498 triangles, and `.chr` now uses a static render fallback. Three synthetic lifecycle, animated-prefab and render-fallback tests pass. This fallback does not follow skeletal animation; actual gameplay remains unverified.
- Game client 3.5 `CryPhysics.dll` is x64, SHA-256 `ef803439f32e373086df744727bbe5a21a074c7f4554bfd61461e9c8e91892eb`. REA/IDA observed exported `CreatePhysicalWorld` at `0x351a79e0` (analysis base `0x35000000`). The 10.x backend has hardcoded RVA/vtable/structure assumptions and requires an exact binary hash: no compatibility with this 3.5 binary has been established. Do not load it into the server without a verified ABI and isolated crash-safe tests.
- Hull wall rays now also query the zone of their endpoint, so a pier in the adjacent zone can be detected before the hull centre crosses the boundary. A synthetic boundary regression and all 24 focused hull tests pass. This is not native's full 512 m cross-zone preloading/copy scheme; unloaded zones still fall back to empty geometry while loading.
- Hull ray contacts choose the nearest hit among the current zone, neighboring zone and doodads. The regression placing a doodad in front of a brush failed before the fix; the Debug build and all 25 focused hull tests pass after it.
- Active hulls now request nearby region zones within a 512 m square once per second, in alternating ship order. A shared owner-counted retention set keeps the union of active zones across instances, including frozen hulls; an idle LRU budget remains available for NPCs. The cache can grow by the number of retained zones and is trimmed when owners release them. Builds remain asynchronous and limited to four, so initial collision can still be missing until loading completes; this has not been validated in gameplay.
- After retention changes, the Debug test project built successfully; all 20 `StaticGeometryManagerTests` passed. The full unit run completed 1,218 tests: 1,215 passed and the same three unrelated failures remain (`BuffTests.Constructor_WithParameters_SetsPropertiesCorrectly`, `BuffTests.AbLevel_SetAndGet_ReturnsCorrectValue`, and `LoginSessionTests.InitiateEnterWorld_ServerNotActive_NoAction`).
- New hulls wait at their seed pose until known zones in the 512 m square have completed loading; they do not collide or catch up the elapsed time while held. Already moving hulls continue across zone boundaries as in upstream. Missing region maps or geometry managers use the existing fallback, and a failed geometry read still completes as empty: neither case proves collision availability. A synthetic blocked-loader integration test exercises release; native and live gameplay parity remain unverified.
- Slave template `obb_size_x/y/z` now reaches fitted hull contacts per individual slave, following upstream's shape and beam scaling. The bundled 3.5 SQLite has nonzero XY bounds for only two ship-kind rows (7 and 829). For missing bounds, a background resolver reads a unique `body` brush or AnimObject, a uniquely named `_body` ship brush, or an otherwise unique brush from the client ship-model directory. It applies node and prefab transforms; ambiguous/missing files retain the reference fallback. Real-client probes covered battle ship, galleon, small boat, clipper, new speedboat, barrel boat, test galleon and an animated fishboat; battle ship 829 agrees with DB dimensions. Animated geometry uses its static mesh pose. This is an approximation and has not been verified in gameplay.
- NPC grounding checked against current ArcheageZones `e869882` and ArcheageServer `f8eb46a65`: a nearby navigation layer now wins over distant lower terrain; position queries select the destination zone; walking checks walls in both zones and invalidates its clear-space probe when the index changes; spawning starts the asynchronous geometry read and does not lower an NPC when no support is known. A bounded tick queue rechecks idle NPCs once geometry loads, including NPCs without AI, and discards moved, hidden, dead, fighting or reparented NPCs; weak references do not retain despawned worlds or NPCs. A real-client probe loaded cell `027_007` in zone 137 and settled a synthetic NPC on an actual brush. Live NPC behavior, structures without brush/nav data and runtime movement remain unverified.
- Hull contacts are now accumulated in the physics backend and delivered to gameplay on the upstream 500 ms grid, preserving the strongest contact and the fitted backend's resting-contact threshold. Previously per-tick draining could discard a stronger impact. The mass supplied to gameplay currently uses the base ship model; ArcheageZones additionally accounts for fitted parts and rider/capacity mass, which remains to port. The Debug build and 31 fitted hull tests passed; full unit run: 1,232/1,235, with the same two Buff assertion failures and one login failure.
- Remaining: finish `.chr`/animated collision, verify and improve ship coefficients, buffs and grounding against 3.5 traces; complete cross-zone preloading, runtime spawn/move/phase/deletion tests, native engine and full NPC/zone simulation scope. No live 3.5 gameplay acceptance has been performed.
