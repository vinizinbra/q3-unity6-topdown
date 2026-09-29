# Mobile Performance — Findings & Improvement Backlog

Running list of performance findings from on-device profiling (Android, Realme RMX3491, ~6 GB RAM,
Vulkan) and the improvements they point to. Each backlog item carries its own plan and status.
Measured numbers are from **Development Builds with the Profiler attached** (they overstate cost
somewhat — ~1.5 ms/frame of pure profiler overhead is visible, e.g. `Profiler.FlushMemoryCounters`).

## How to profile (workflow that works)

- **Connect over USB, not Wi-Fi.** Pick the device's **(ADB)** entry in the Profiler target
  dropdown. Wi-Fi drops/delays frames under load — a 2026-09-27 capture lost ~21 s exactly during the
  heavy wave it was meant to catch.
- **No Deep Profile.** The normal capture already shows every MonoBehaviour `Update() [Invoke]` and
  every Quantum system marker. Deep Profile needs a *Deep Profiling Support* build (the Editor
  toggle alone does nothing on device), inflates small hot calls, and shrinks the 2000-frame buffer's
  time window. Add `ProfilerMarker`s to specific code instead when a marker is too opaque.
- **GC hunting:** Profiler → *Call Stacks → GC.Alloc* (much lighter than Deep Profile).
- **Logs are off on device by default.** `LogHelper.Disabled` defaults to `true` outside the Editor
  (`LogHelper.cs`); flip it in the SRDebugger Options panel when device logs are needed. With logs
  on, a Development Build pays a script stack trace per log (~60 ms per logging frame, 1–4 s spikes).
- The buffer holds ~2000 frames (~45 s): reproduce the scenario, **stop Record right after**, then
  analyze. Claude reads the buffer from the live Editor via `unity cmd eval` (ProfilerDriver /
  HierarchyFrameDataView), in ≤40-frame batches because the Editor caps each call at 5 s of
  main-thread time.
- **Memory:** the Profiler's memory counters give totals only; use the Memory Profiler package
  (installed) → target the device → *Capture* for a per-object breakdown, and compare two
  snapshots (menu vs gameplay, early vs late run) for leaks.

## Findings so far (2026-09-27)

- **Logs were the #1 stutter source** (fixed by the device default above). Sources that survive
  Release or bypass `LogHelper`: PrimeTween "tween on inactive target" warning (`HeadHat`),
  `VoiceDirector` error on every HeavyHitReceived (no voice bank for the speaker),
  `HasBuildingShadow.AcquireWithRetries` logging inside its retry loop.
- **Baseline frame with ~0 enemies: ~24 ms p50 (~41 fps), main thread busy ~19 ms** (dev build +
  profiler). Spread thin: `QuantumRunnerBehaviour.Update` ~7 ms (sim ticks ~3.9), other
  `Update`s ~3.7, `FinishFrameRendering` ~4.2, `LateUpdate`s ~2.5, UI canvases ~1.3.
  Render thread only ~2 ms real work (+~5.5 ms blocked in `GfxDeviceVK.Present`); ~150–230
  batches, 35–53k tris — GPU side looks light but GPU time is not captured on this device.
- **Many-monsters drop (60 → 40 fps) is NOT yet captured.** Both captures so far only reached
  ~30 extra entity views (baseline 142). Needs a USB capture at a large wave.
- **Memory:** process ~870 MB, Unity Total Used ~666 MB; ~250 MB and +34k Objects arrive at level
  generation; ~350 MB of Total Used is not in any tracked category. Android fired
  `APP_CMD_LOW_MEMORY` twice (device-wide signal; not proof we're the cause). Needs a snapshot.

## Backlog

### 0. Cut rollback cost (the many-monsters drop) — PLANNED, TOP PRIORITY

**2026-09-27 "bad areas" capture (alone, Breathing, 0 enemies):** the drops were NOT render —
SetPass 40, ~42k tris, `FinishFrameRendering` ~3 ms, all flat. Slow frames (28–32 ms) are exactly
the frames resimulating **10–12 ticks at once** (~1.6 ms/tick of fixed overhead → 16–19 ms); in
between frames run ~17 ms. **152.8 ticks/s simulated for a 20 Hz sim (7.6×)**, i.e. a ~500–600 ms
prediction window at ~80 ms ping. Confounder: the Profiler was streaming over the same Wi-Fi as
the game (`AndroidPlayer@192.168.1.79`) — previous capture measured 92 ticks/s. Next: read
`QuantumStats` (CheatMenu → "Quantum Stats": Ping / Predicted / Input Offset) on device **without**
the Profiler attached; if Predicted stays ~2–4, the deep bursts were the profiling link.
**Confirmed by the user: attaching the Profiler over Wi-Fi caused visible packet loss.** Every
Wi-Fi capture so far overstates rollback depth (ticks/s, burst size, and the Min 1 input-delay
before/after comparison). Per-tick and per-enemy costs are still valid. **Profile over USB (ADB)
only** from now on.

**Why.** Confirmed 2026-09-27: offline (Local, no rollback) runs the same waves noticeably smoother.
Online, ~35% of frames resimulate 5–17 ticks at once, so every per-enemy cost is paid 5–17×.
Quantum's own docs describe this exact case ("a 30Hz game rolling back ten ticks ... must run at
almost 300Hz"). The project has **no Prediction Culling**: `CullingSystem3D` is not registered in
`SystemSetup.User.cs` and nothing calls `SetPredictionArea`.

**Rollback cost = (cost of one tick) × (ticks resimulated per rollback).** Attack both factors.

**Plan, in order of payoff** (revised 2026-09-27: enemies despawn when far, so nearly all live
enemies are on-screen and Prediction Culling helps little — demoted to last).
1. **Shrink the resim count** — measure first (step 2 below), since a wrong region or input-delay
   config may be most of it.
2. **Make one tick cheaper** — the per-tick total, not just `EnemySystem`:
   - Rank systems by real per-tick cost vs enemy count (normal capture, Main Thread + `Worker N`
     threads). An Editor Play Mode capture with a cheat-spawned wave gives a valid *relative*
     ranking without the phone.
   - Throttle per-enemy decisions deterministically (target selection, `CanBegin` scans, the
     `UseWaypointDetour` line-of-sight raycast) to every N ticks, staggered by entity index —
     cheaper in both verified and predicted ticks, no prediction divergence.
   - Disable systems that can't do anything in the current state (lobby/tutorial/talent/level
     generation/boss-only systems) via system groups — a disabled system costs nothing per resim.
     ~84 systems run every tick today.
   - Move heavy, write-local per-enemy systems from `SystemMainThreadFilter` to
     `SystemThreadedFilter` so they spread over the phone's cores instead of the main thread.
   - Shrink the frame snapshot (`Copy Heap` / `Copy Component Data` grow with state size).
   - Skip work whose result is never visible until verified (e.g. stats/bookkeeping) with
     `f.IsVerified` — only where divergence can't cause a visible snap.
3. **Prediction Culling (demoted).** Only pays off when co-op players spread apart. Register `Core.CullingSystem3D`; every frame on the view side call
   `game.Frames.Predicted.SetPredictionArea(center, radius)` (it's main-thread safe), with center =
   local player (or camera focus) and radius = visible screen area + margin (≥ longest weapon/skill
   range, so what the local player can hit is always predicted). Entities outside are simulated
   once per tick when input is verified, never rolled back. Filters (`SystemMainThreadFilter`,
   `f.Filter`) skip culled entities in predicted frames by default.
   - Audit: code that reaches a culled entity through a direct pointer during a predicted frame
     (enemy targeting a far player, projectiles/areas hitting a culled enemy, director/cluster
     utilities that scan all enemies), events raised on predicted frames, and views of culled
     entities (update at the verified rate; they're off-screen). Bots: one area per *local*
     player; with several local players use a center/radius covering all of them.
   - Verify: online co-op at a big wave; nothing visible snaps; frames with 5–17 ticks should
     drop sharply in sim cost.
4. **Measure why the prediction window is so deep** (do this first — feeds step 1). 5–17 ticks at 20 Hz = 250–850 ms. Show
   Quantum's `QuantumStats` (ping, predicted frames, resimulations) in dev builds (SRDebugger
   toggle) and check the chosen region — a far region inflates every rollback. Fixing latency
   shrinks every resim.
5. **Input delay — DONE 2026-09-27.** `InputDelayMin/Max` are in **ticks** (1 tick = 50 ms at
   20 Hz). Was Min 0 / Max 60 (the SDK default Max, meant for 60 Hz = 1 s, which became 3 s at
   20 Hz). Now **Min 1 / Max 6** (always ≥50 ms, ≤300 ms at high ping), Ping Start unchanged at
   100 ms. Min 1 removes ≥1 resim tick from every rollback; lowering Max alone would not help perf
   (less delay → more prediction). Feel-test the local character's responsiveness online.
   - Open question: the user plays at ~80 ms ping (Europe), which predicts only ~2–4 ticks at
     20 Hz — yet captures showed 5–17 resim ticks per frame. Suspects: the phone's Wi-Fi jitter,
     and the Profiler streaming over the same Wi-Fi during capture. `QuantumStats` on the phone
     (and a USB-connected capture) will tell.

### 1. Unity physics: stop simulating, cache static ground raycasts — PLANNED

**Why.** Gameplay physics lives in Quantum; Unity PhysX is used *only for queries* — ~15 view
scripts raycast down onto the Ground layer (`GroundBlobManager`, `CharView`, `PlayerShadow`,
`SnapToGround`, `MechanicalLegRig`, `ProceduralTentacleWalker2D`, `EffectsManager`,
`GroundWarningTelegraphManager`, `RingWaveVisualManager`, `EnemyAttackVisualsView`,
`BossArenaGateVisualsView`, `GroundDecal`, `BuildingShadowManager`, `BlobAnimationView`). No runtime
Rigidbody / trigger / collision callbacks exist (`ChunkWaypointBaker` and `HasBuildingShadow`'s
`SyncTransforms` are bake-time only; AntiCheat's WallHackDetector is unused). Yet
`DynamicsManager.asset` has `m_SimulationMode: 0` (FixedUpdate), so PhysX steps every fixed tick.

**Measured cost.** `PxScene.simulate` + `FixedUpdate.PhysicsFixedUpdate` ≈ 0.4–0.8 ms/frame;
`GroundBlobManager.LateUpdate` 0.5–0.9 ms total with 107 raycasts/frame at zero enemies (149 with
some). Expected win ≈ 0.7–1 ms/frame. Worth doing, but **not** the cause of the many-monsters drop.

**Plan.**
1. **Audit runtime colliders first** (Play Mode, via Editor eval): count colliders by layer and
   find any whose transform changes after spawn (moving platforms, Traversal Challenge platforms,
   boss arena gates/seal, anything under entity views). This decides step 3.
2. **Switch simulation off:** `Physics.simulationMode = SimulationMode.Script` in
   `DynamicsManager.asset` (project-wide; the Editor preview scenes only raycast too).
3. **Keep query results correct without the step.** `m_AutoSyncTransforms` is 0, so moved or newly
   spawned colliders only reach PhysX on a simulate or an explicit sync. Call
   `Physics.SyncTransforms()` once after level generation completes (the LoadingWindow hand-off is
   the natural point), plus once per frame *only if* step 1 found colliders that move at runtime
   (cost scales with changed transforms, so it's cheap when nothing moved). If only a few things
   move, sync on their own events instead.
4. **GroundBlobManager: skip the raycast for unmoved targets.** Store `LastTargetPos`, `LastHit`,
   `LastHasGround` on `GroundBlobHandle`; reuse the cached hit when the target's position is within
   a small epsilon of last frame. Still recompute scale/alpha every frame (lossyScale can animate,
   e.g. spawn scale-in). Invalidate all caches when the ground can change (level generated, boss
   arena seal/unseal, traversal platforms) via a static `GroundVersion` counter those events bump.
5. **Optional follow-up:** batch the remaining per-frame raycasts with `RaycastCommand.ScheduleBatch`
   to take them off the main thread — only if the many-monsters capture shows raycasts scaling.
6. **Verify:** Editor Play Mode (shadows sit on ground, fall off edges, re-land on platforms; boss
   arena; traversal challenge), then a USB device capture comparing `PxScene.simulate` (should be
   gone), `Physics.Raycast` call count, and `GroundBlobManager.LateUpdate` against the numbers above.

**Risks.** A collider that moves without a sync gives stale raycasts (shadow floating/missing) —
covered by steps 1 and 3. A future feature relying on OnTrigger/OnCollision or Rigidbodies would
silently stop working; the comment on the settings change must say simulation is off on purpose.

### 2. Capture and diagnose the many-monsters drop — IN PROGRESS

USB capture during a large wave (20–30 s at the low fps), then split frames by enemy count and diff
per-system cost. Best enemy-count proxy: `EnemySystem.Update` call count ÷ ticks (it's a
`SystemMainThreadFilter`, so it's called once per enemy per tick). Quantum systems run on the
Main Thread **and** Unity `Worker N` threads — analysis must include both.

Findings so far (2026-09-27):
- **Rollback multiplies every sim cost.** Sim runs at 20 Hz (`SessionConfig.UpdateFPS`), yet in
  an online (Multiplayer) session ~35% of frames ran 5–17 ticks in one frame (resimulation of
  predicted ticks when verified input arrives), and the sim alone cost 10–20 ms in those frames.
  Every per-enemy cost is paid again on each resim tick. `InputDelayMin: 0`,
  `InputDelayPingStart: 100` → no input delay below 100 ms ping, so the prediction window is the
  full latency. **Verify:** same wave in Local mode (no rollback) — if fps holds, rollback is the
  multiplier. Levers: input delay, prediction culling, skipping non-essential work on predicted
  frames, cheaper per-tick systems. **Confirmed** by the user: Local mode runs noticeably smoother
  → see backlog item 0.
- **Fixed per-system overhead:** ~84 systems run per tick (53 are `SystemMainThreadFilter`s, each
  building a filter every tick even when it matches nothing).
- **Real per-tick costs (normal capture, main thread, few enemies):** frame snapshot copies
  (`Copy Component Data` / `Copy Heap` / `Copy Entity Info`) ~0.31 ms, Quantum physics
  (`IntegrateForces`, `BroadPhase`, `CreateEntries`, `EntryPreStep`) ~0.37 ms, `OnSimulateBegin`
  0.07, `KCCSystem` 0.03, `EnemySystem` 0.02.
- **Systems to size in the heavy capture** (from a deep frame): `EnemySystem` (per enemy: targeting,
  movement, `CanBegin` per action, and a line-of-sight wall raycast every tick for
  `UseWaypointDetour` enemies), `KCCSystem`, `StatusEffectSystem`, `GroundSettleSystem`
  (~88 entities/tick), `EnemyLifecycleSystem`, `AimSystem` (enemy `OverlapShape`), `WeaponSystem`,
  `ProjectileSystem`, `EnemyFallSystem`, `EliteRelocationSystem`.
- **Sim stage markers (added 2026-09-27)** via `SimProfilerMarker`
  (`Systems/Debug/SimProfilerMarker.cs`): Quantum `HostProfiler` markers, `[Conditional
  ("ENABLE_PROFILER")]` so they vanish from Release players. No Quantum debug DLLs or Deep Profile
  needed — a normal Development Build shows them under each system's own marker:
  - `EnemySystem.{FallCheck, Setup, StuckRecovery, StatusQuery, KnockbackRecovery, Idle, Chasing,
    Preparation, Active, Recovery, ResolveTarget}` and
    `EnemySystem.Chasing.{TraversalJump, Decoy, SelectAction, Detour, MoveDirection, Move}`
  - `StatusEffectSystem.{Burn, Ice, OverloadChain, Timers, PlayerTimers}`
  - `GroundSettleSystem.FindGround`, `EnemyLifecycleSystem.{RecentCombat, AdvanceState, Retire}`
  - `AimSystem.ResolveTarget`, `ProjectileSystem.{UpdateVelocity, CastForHit, ApplyHit}`,
    `WeaponSystem.{Ticks, FireShot, FollowUpShots}`
  - Not instrumented: `KCCSystem` (Photon addon, players only), `EnemyFallSystem` /
    `EliteRelocationSystem` (per-enemy body is a `FindAsset` + early return — their cost is the
    per-system iteration itself; candidates to fold into `EnemySystem` or narrow with a tag
    component).
- **Measured per enemy per tick (2026-09-27, ~7.5 enemies, dev build):** `EnemySystem` ≈ 44 µs —
  `Chasing` 18.8 (`Move` 7.7, `Detour` 2.8, `SelectAction` 2.2), `KnockbackRecovery` 9.5 (an
  `IsGrounded` raycast every tick), `Idle`+`ResolveTarget` ~5.5. Fixed per-tick ≈ 1.8 ms (engine
  copies+physics ~0.77, ~84 systems ~1.2). A chasing enemy fires ~5–8 Quantum raycasts per tick.
  Projection at ~50 enemies: ~5 ms/tick × ~2.5–2.8 ticks/frame ≈ 12–14 ms/frame of sim.
- **Input delay Min 1 measured:** simulated ticks/s 121.5 → 92.4 (−24%), median rollback burst
  8 → 6 ticks (still ~4.6× the 20 Hz rate).
- **Fix 1 DONE — IsGrounded memo:** `EnemyMovementUtility.IsGrounded` reuses the result for the same
  entity/position/mask within one enemy's `EnemySystem.Update` (`BeginGroundMemo`/`EndGroundMemo`,
  try/finally). Removes the duplicate probe between `TickKnockbackRecovery` and `MoveInDirection`
  (and SmartFlee's). Deterministic: identical query, same tick, nothing in between moves the
  entity or rebuilds the physics scene; statics are safe because no sim system is threaded.
- **Next candidates:** throttle `SteerAroundWalls` / climb / ground-ahead probes (every 2–3 ticks,
  staggered, cached) — changes behaviour slightly; disable state-irrelevant systems.
- The deep-profile capture was unusable for timing: seconds-long single samples (stalls), and
  spin-waits (`MemoryBarrier`, `Segment.TryPeek`) dominated.

### 3. Memory snapshot — TODO

Memory Profiler capture on device mid-gameplay: explain the ~350 MB untracked, the +34k Objects at
level generation, and compare early vs late run for growth.

### 3b. View-side LateUpdate hot paths — DONE 2026-09-27

Normal-capture cost before (per frame): `GroundBlobManager.LateUpdate` 0.5–0.9 ms, `Billboard`
~0.28 ms (~90 instances), `CharacterUiWidget.LateUpdate` ~0.18 ms (8 widgets) — Deep Profile made
these look 10–50× bigger (every `.transform` / getter is instrumented). Changes:
- `GroundBlobManager`: blob `Transform` cached on `GroundBlobHandle`; target position and hit
  point read once per blob.
- `Billboard`: own transform cached; the camera-facing rotation is computed once per frame and
  shared by every instance (static, keyed on `Time.frameCount`).
- `CharacterUiWidget`: status timers (`SetSeconds`/`SetCount`) and health/shield `x/y` labels only
  rewrite when the displayed number changes, via allocation-free `TMP_Text.SetText(format, …)`;
  `StatusEffects` passed `in` instead of copied into 11 methods.
Further step if still visible: fold `Billboard` into a manager loop (one Unity message instead of
~90), same pattern `GroundBlobManager` already uses.

### 3c. GrassOutpost "bad areas" were GPU-bound — barbed wire suspected (2026-09-27)

USB capture, alone in Breathing, alternating areas: sim/CPU/SetPass flat; 50-fps areas had the
main thread waiting ~4–5.6 ms on the GPU (present ~5–6 ms vs ~1.3) and **triangles 17k → 30–39k**.
Suspect: the GrassOutpostV2 **BarbedWire** wall run — a concertina helix (5 turns/cell × 12
rings/turn × 8-sided tube, radius 2.4 cm) ≈ **960 tris per cell**, ~2–3 px wide at the gameplay
camera → nearly all sub-pixel triangles (worst case for mobile GPUs; median prop = 108 tris).
Wire left as authored (Chance 0.18). A/B on device was done with a (since removed) "Hide Barbed Wire" toggle
(disables renderers of `*_Run_Wire*` instances under `*_TilesetVisual`; chunks built after the toggle
need it re-toggled). If the 50-fps areas return to 60 with it hidden, rebuild it cheaply: alpha-clipped textured ribbon (ink baked in, mips with Preserve Coverage; stakes/cans/
sign stay geometry), or at least `tube(segs=4)` + 6 rings/turn in `build_grasscliff_autotiles.py`
(~240 tris/cell). Other ~1k-tri runs: Favela `Run_Gamb_*` (Knot 3,940, Poste 2,396).

**Result (device A/B, 2026-09-27):** hiding the barbed wire = 49 → 51 fps only. CheatMenu →
Rendering → **"Simple Terrain Shader"** (swaps every `RiftRaiders/Test/ToonTerrain` material for
`RiftRaiders/Debug/SimpleVertexLit`, per-vertex lit, flat fragment; `Resources/Debug/DebugSimpleVertexLit.mat`)
**fixes the drop** → the area drop is ToonTerrain's per-pixel cost (it covers nearly the whole screen:
tiles + V2 props). The Hide Wire / Runs / Props / Terrain Tiles toggles used for this were removed afterwards.

**ToonTerrain fragment optimized (same output):** props skip the terrain albedo they discarded; the
hatch texture is only read where `darkness > min(Hatch1, Hatch2)` (below, both smoothsteps are exactly
0) via `SAMPLE_TEXTURE2D_GRAD` with derivatives taken outside the branch; prop edge lines only on
meshes with edge data (terrain paid an `fwidth(float3)` per pixel for nothing); emissive / raised tint /
water gradient only when active; colour maths in `half`. GrassOutpostV2_Toon has nearly every feature
on (gradient, wall line, border + 8 px strata outlines, hatch), so skipping disabled features alone
wouldn't have helped. Needs a device re-measure vs the Simple shader ceiling; next levers if still
short: per-vertex `ndl`/lit for the flat-shaded tiles, cheaper outlines, render scale 0.7 on low-end.

**`ToonTerrainMobile` (2026-09-27):** separate mobile shader, same Properties/cbuffer — lighting +
ambient collapse to one per-vertex multiplier (albedo factors out), hatch weights per vertex, all
position-linear terms interpolated, no shadow variants; fragment = one albedo read + hatch read on
shadowed faces + pixel-width lines. Visual diff vs pre-optimization ToonTerrain (PreviewRenderUtility,
gameplay camera, tiles + props + wire): 5/630k pixels differ, max 10/255. A/B on device via CheatMenu →
Rendering → "Mobile Terrain Shader" (material copies with only the shader swapped). If it holds 60 fps
and looks right, switch the biome `*_Toon.mat` materials to it (PC keeps ToonTerrain if it needs
received shadows — e.g. a mobile-only material swap at startup, or per-platform materials).

**Device result:** Mobile shader improved fps but the Simple (flat) shader is still faster. Textures are
not the issue (grass.png doubles as surface + wall, hatch 1254² → 512 compressed on Android, bilinear,
no aniso, mips). Remaining cost is fragment ALU. Measure per feature on device: CheatMenu → Debug tab →
**"Terrain Cost (Mobile shader only)"** toggles (globals `_TTDebugNoAlbedo / NoHatch / NoOutlines /
NoPropLines / NoWater`, uniform branches, ~free at 0) + **render scale 0.6 / 0.7 / 0.8 / 1.0** buttons
(device only - in the Editor it would write the URP asset). The water-line `fwidth` now only runs when
the line is on.

**Device result (2026-09-28):** in a 55-fps spot, turning off ANY one of albedo / outlines / prop lines /
water alone reached 60 (hatching helped least) → cost is the sum, not one feature. Second mobile pass,
all exact early-outs (render diff vs pre-optimization ToonTerrain unchanged: 5/630k px, max 10/255):
- terrain outlines: pixel distance to the nearest line first (derivative outside the branch); the
  width/strength selection only runs within `max(width)/2 + 0.5` px of a line - elsewhere it's 0;
- water gradient: full ramp only inside the band; above it (ground and most of the walls - GrassOutpost
  band is Y -3.22..-1.22) it's `color *= lerp(1, Top, S)`;
- water line: smoothstep/lerp only on wall pixels within `lineHalf + AA` of the line;
- raised tint only when `_RaisedStrength > 0`; hatch derivatives only when hatching is on.
- All shader passes together: **~+4 fps** on device (2026-09-28).
- Render scale 0.6 was tried as the Mobile default and **reverted to 0.8**: render scale applies to the
  whole 3D camera (hero, enemies, VFX - only screen-space UI is exempt), and the hero visibly degraded.
  CheatMenu → Debug tab now also switches the upscaling filter (Linear / Point / FSR, device only) to test
  e.g. 0.7 + FSR before touching the default again.

**Shipped 2026-09-28:** every biome material (20) now uses `ToonTerrainMobile` (see
docs/tileset-builder.md). CheatMenu's "Mobile Terrain Shader" swap and its Resources material were
removed; "Simple Terrain Shader" + "Terrain Cost" now operate on the mobile shader.

### 3e. Frame Debugger findings — blob shadow/light batching fixed (2026-09-28)

Frame Debugger is readable from the Editor via reflection (`UnityEditorInternal.FrameDebuggerInternal.
FrameDebuggerUtility`: `GetFrameEvents`, `limit`, async `GetFrameEventData` - select an event, wait an
editor update, then read shader/pass/verts/batch-break cause). Editor Play Mode capture, 100 events:
the whole opaque world is **1 ToonTerrain SRP batch** (56 draws, 45k verts); the rest is transparent
SpriteRenderers + UI. Blob **shadows (18) and blob lights (Sprites/Default, 4-vert quads) alternated**
shadow/light/shadow... - same prefab sorting order (-1), so the transparent pass sorted them by
distance and every material switch broke the batch. Fix: `GroundBlobManager` sets
`shadowSortingOrder` (-4) / `lightSortingOrder` (-3) on every Acquire (pooled instances switch role) →
all shadows batch, then all lights, below DeathDecal (-2), VFX (-1) and characters (0+). Also seen:
`Sprites/Default` isn't SRP-Batcher compatible; UI/Default and TMP text alternate per widget (→ 3d).

### 3d. UI cost / popup drop — TODO (separate from the area drop)

User-observed: opening a popup drops fps to ~45. Baseline UI CPU in captures ≈ 1.4 ms/frame
(`PlayerUpdateCanvases` ~1.1, `UGUI.Rendering.UpdateBatches` ~0.3). Not the cause of the area drop
(HUD is identical across areas; SetPass flat). To investigate: capture over USB with a popup open —
look for full-screen transparent overdraw (backgrounds, dimmers, blur), canvases rebuilding every
frame (animated/tweened elements forcing layout/mesh rebuilds), and nested-canvas splitting.

### 4. Silence Release-surviving log spam — TODO

Fix the root causes listed under Findings (PrimeTween inactive-target tween on `HeadHat`,
`VoiceDirector` per-hit error, `HasBuildingShadow` retry-loop logging). Decide whether
`LogHelper.Error` should bypass the `Disabled` kill switch (currently it is muted on device too).
