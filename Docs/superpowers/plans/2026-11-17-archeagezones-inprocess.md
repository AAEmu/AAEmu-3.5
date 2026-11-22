# ArcheageZones in-process migration

> **For agentic workers:** Execute this plan task-by-task using superpowers:executing-plans. Independent geometry, ground and hull work is delegated under superpowers:dispatching-parallel-agents.

**Goal:** Remove Jitter2 and the old physics, port ArcheageZones simulation directly into the AAC3500 server.

**Architecture:** Keep 3.5 client serialization and gameplay models. Port domain algorithms (geometry, ground/water, hull bodies, contacts and fixed steps) with direct calls from WorldInstance. No zone process, zone TCP, ZW/WZ messages or 10.x wire types. Hull physics remains behind an internal IHullPhysics contract.

**Tech Stack:** .NET 10, C# 14, System.Numerics, TUnit.

**Spec:** User's explicit goal and follow-up in this chat; upstream ArcheageZones at db08214. Existing reports describe the earlier Jitter2 bridge and are superseded where incompatible with this goal.

GlennDepreySoftware (Glenn Deprey) and PLWatt90 (Ookimus) are co-authors of the port.

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

## 2026-11-22 hull verification

- Compared hull construction with ArcheageZones `ca2edc4`. AAC3500 derives additional mass from mounted parts and riders. The managed backend applies linear impulses and yaw inertia, reads named box floats, and computes displacement for fully specified horizontal cylinders when the prefab has no named floats.
- REA confirmed circular-segment displacement in `CCylinderGeom` at RVA `0x143230` of the r575 `cryphysics_dedicate.dll`. The server implements that calculation in C#. The Debug dependency manifest confirms a managed-only hull backend.
- In the 3.5 data, 25 spawnable models qualify for an authored cylinder. With the water surface at Z=100, the unpacked-client probe settled the rowboat at 99.330 m and the galleon at 100.567 m. Adding half their base mass lowered them to 99.240 m and 100.431 m respectively. The clipper used its named boxes despite having cylinder parameters.
- A zero cylinder field now takes its value from the combined render bounds of the prefab's brushes and AnimObjects. Each model supplies its occupied mesh bounds in object space. An AnimObject can reference its model through a CDF. Half-width supplies the missing radius or vertical offset, and half-length along Y supplies the missing cylinder half-length.
- Checked all 58 rows of `ship_models` in the bundled 3.5 database against the unpacked client. Of these, 44 use cylinders (11 derive at least one field), five use named floats, and nine use fitted heave. In the packed client, fishboat 19 spans Y from −15.768924 m on its animated model to +15.962001 m on its brush. Its cylinder half-length is 15.865462 m. Battle ship 55 derives 13.769541 m from a single brush.
- Debug build passed. The physics suite passed 132/132. The full unit suite passed 1,364/1,373. Eight failures require zone 129/179 editor data and one is `InitiateEnterWorld_ServerNotActive_NoAction`.

### Keel, trim and water resistance

- The keel is a flotation box with half-width 0.0001 m. It uses the placement and dimensions in ArcheageZones `HullBuilder.Plan`. The lowest brush bound sets the vertical reference. Models with authored keel dimensions also work when their prefab has only animated objects.
- REA verified the r575 rigid-body buoyancy step at RVA `0xc0e50` and the box's wetted-face resistance at `0x13e7c0`, `0x133e50`. The C# implementation integrates positive normal velocity over the wet portions of the surface. Buoyancy acts at the submerged volume centroid. Resistance and buoyancy produce torque around the mass-box centre.
- The cylinder's submerged volume and first moment use circular cross sections along its axis. Resistance uses a 32-sided cylinder surface. Pitch and roll use authored inertia and reach the existing 3.5 movement report as quaternion and world angular velocity.
- The calibrated surge, sway and yaw equations still drive horizontal handling. An offline probe of the coupled alternative produced excess turn rates and excessive roll on the rowboat. The geometric forces therefore drive heave and tilt, with pitch and roll capped at ±1.2 radians. The remaining parity work includes the native speed feedback and rudder forces, then validation of the coupled planar forces.
- Offline probes used stock 3.5 prefabs and the bundled database for models 3, 4, 6, 19 and 55. An angular velocity impulse of 0.35 rad/s about each tilt axis produced 2.04° to 16.86° of peak tilt. In the managed simulation the stock rowboat completed a sustained full-helm turn with about 0.02° of tilt. Live gameplay remains unverified.
- The Debug build and all 141 physics tests passed. The full unit suite passed 1,373/1,382, with the same nine failures.

### Engine, rudder and coupled water forces

- The in-process controller uses the throttle and rudder equations from ArcheageZones `ca2edc4`. The 3.5 ship row supplies acceleration, reverse acceleration, halt rate, engine speed limits and RPM response. Slave, equipment and buff modifiers supply speed and rudder attributes. Percentage bonuses add before scaling the flat value, and the controller reads applicable bounds from `unit_attribute_limits`. The slave's facet formula runs at spawn and supplies the base for steering and reverse-speed multipliers.
- The controller rotates the bow and stern points into world space and tests each point against the world's ocean and inland water areas. The managed body integrates the rudder force with pressure on the wet surfaces and blends rigid-body and water damping by the submerged volume fraction.
- Each 30 Hz step in authored mode integrates two 16.7 ms substeps. Terrain, brush and doodad contacts resolve after each substep, while hull pairs resolve at the world tick. Regression tests cover the wet and dry sides of a lake boundary and a thin pier that the hull would otherwise cross between contact checks.
- The full-helm probe evaluated all 58 ship model rows with normalized speed and turn attributes. The managed controller ran for 47 models. The remaining 11 lacked the required geometry in the unpacked 3.5 client. Models 32–35 stayed at zero forward speed as configured. The wide 800 kg model 29 stabilized at Z=99.993 m over water at Z=100 after reducing the step. That model has no matching slave template in the stock database.
- At normalized speed and turn attributes, the 3.5 clipper with 4,600 kg of equipment reached 11.02 m/s straight ahead. The loaded galleon with 22,450 kg reached 10.00 m/s. Repeating with the unpacked r575 geometry gave 10.96 m/s and 10.00 m/s respectively. The upstream native reference tests report 10.43 m/s and 9.99 m/s for the corresponding cases.
- The controller remains opt-in through `World.AuthoredShipDriveEnabled`. Models lacking the required input use the calibrated drive when this setting is enabled. The clipper's remaining speed difference and actual 3.5 client handling still need live validation.
- The Debug build passed. The physics suite passed 161/161. The full suite passed 1,395/1,404 with the same nine failures.

### 2026-11-23 shared steps and packed-client verification

- `AuthoredShipDriveEnabled` puts both authored and fitted hulls on a shared 60 Hz grid. The pair solver sweeps both footprints over the same time interval and intersects their vertical overlap with the planar contact interval. It moves the hulls horizontally back to first contact before applying the impulse. The sweep uses the yaw at the end of the substep and the existing fitted collision dimensions. The default mode keeps the 30 Hz fitted path. In both modes, a dead hull loses powered thrust and rudder forces.
- A complete authored cylinder now works without a prefab or brush bounds. Models 47 and 48 use animated bounds for flotation. Model 26 has all three cylinder dimensions in `ship_models`. The production resolver and drive factory found enough data for 50 of 58 model rows in each of the two 3.5 `game_pak` archives. Models 1, 2 and 5 reference missing meshes. Models 8, 23, 24, 38 and 56 reference missing prefab entries. These eight use the fitted drive.
- The offline probe with normalized drive attributes, r575 geometry and rows measured a loaded clipper waterline of 99.591 m at rest and 99.644 m under full throttle, with the sea at Z=100. The upstream native test reports 99.5916 m at rest and 4.5 mm of rise per m/s. The managed clipper reached 10.96 m/s against the upstream 10.43 m/s. The managed and upstream galleon reached 10.00 and 9.99 m/s. REA identifies the buoyancy vector in the call from rigid-body step RVA `0xc9df0` to `0xc0e50` as constant gravity `(0, 0, −9.81)`, which matches the managed separation of thrust and buoyancy. The measurements do not isolate the remaining clipper speed difference. The 3.5 client handling still needs live validation, and the authored drive remains opt-in.
- The Debug build passed. The physics suite passed 172/172, including head-on, mixed-controller and vertically separated hulls, the missing prefab, and the dead fitted drive. The full suite passed 1,406/1,415. Eight existing failures require zone 129/179 data, and one is `InitiateEnterWorld_ServerNotActive_NoAction`. The game dependency manifest contains no CryPhysics, ArcheageZones or Jitter2 runtime reference.
