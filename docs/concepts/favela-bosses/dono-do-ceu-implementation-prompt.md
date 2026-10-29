# Implementation prompt — Dono do Céu

Implement **Dono do Céu**, the Favela world's flying vulture boss, in this Unity 6 / Photon Quantum project. Build the complete playable boss using our current boss structure, existing approved sprites, and existing whirlwind particles. This is implementation work, not another concept-art pass. Inspect the current working tree and applicable repository instructions first; preserve unrelated work. Continue through asset authoring, integration, and appropriate validation.

## Existing architecture to follow

Read these before designing new systems:

- `docs/boss-encounter.md`
- `docs/riff-riot-boss.md`
- `Assets/_Project/Editor/Boss/RiffRiotBossBuilder.cs`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Boss/BossDataAsset.cs`
- `Assets/_QuantumUser/Simulation/Systems/Enemy/BossSystem.cs`
- `Assets/_QuantumUser/Simulation/Systems/Enemy/BossPhaseUtility.cs`
- `Assets/_QuantumUser/Simulation/QTN/Enemy/BossRuntimeState.qtn`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/EnemyActionData.cs`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/EnemyActionData.View.cs`
- `Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/AttackVisualStep.cs`
- `Assets/_QuantumUser/Entities/Enemies/Neon-RiffRiotPrefab.prefab`
- `Assets/_QuantumUser/Resources/Enemy/Neon/Boss/RiffRiot/`

Use `BossDataAsset`, `BossRuntimeState`, `EnemyActionData`, reusable deliveries, existing telegraphs, phase pools, stagger, boss HUD, intro/defeat sequences, rewards, and debug encounter entry. Do not build a parallel boss controller or drive gameplay from a MonoBehaviour. All targeting, damage, timers, movement, hazard ownership, and random choices must be deterministic Quantum simulation; Unity handles presentation only.

Use an idempotent editor builder similar to Riff Riot's: stable Unity/Quantum identifiers on reruns, isolated copies of assets that need tuning, no modifications to W1/W2/Neon content or Yoru's skill tuning. Keep generated tuning in one documented source of truth. Add only the smallest reusable extensions needed for this kit; default behavior of existing bosses must remain intact.

## Identity and approved artwork

Large compact anthropomorphic vulture, red-and-black striped shirt, ivory head, yellow hooked beak and talons, broad black/red geometric wings, large magenta Rift crystals, and a silver referee whistle. Maintain the approved angular, readable style. No crown, football, guitar, or speaker mechanics.

**Use these exact source images:**

- Boss sheet A: `Assets/0_Refs/FavelaBoss/DonoDoCeu-v3-SheetA.png`
- Boss sheet B: `Assets/0_Refs/FavelaBoss/DonoDoCeu-v3-SheetB.png`
- Four trash variants: `Assets/0_Refs/FavelaBoss/CrowdTrashSpriteSheet-v1.png`
- Visual concept reference only: `docs/concepts/favela-bosses/02-dono-do-ceu.png`
- Pose documentation: `docs/concepts/favela-bosses/dono-do-ceu-v3-sheets.md`
- Trash documentation: `docs/concepts/favela-bosses/crowd-trash-sprites.md`

Use v3 sheets, not the older v1/v2 pose sheets. Inspect the images and alpha before slicing. These are generated source sheets: do not assume a blind equal-grid cut cleanly isolates every extended wing or that the alpha halo is already production-ready. Preserve reference originals; create derived production sheets/sprites as needed. Keep full wing tips, remove background contamination/halo from derived assets if present, and align body scale and logical root pivots between poses. Broad wings must not inflate the physical body collider. Match the existing rig's sprite-fit, orientation, material, shadow, and height conventions; airborne sprite changes must not double the simulation's vertical movement.

Suggested output locations:

- Art: `Assets/_Project/Art/Sprites/Enemy/Favela/DonoDoCeu/`
- Boss assets: `Assets/_QuantumUser/Resources/Enemy/Favela/Boss/DonoDoCeu/`
- Entity: `Assets/_QuantumUser/Entities/Enemies/Favela-DonoDoCeuPrefab.prefab`
- Boss-specific VFX variants: `Assets/_Project/Prefabs/Effects/DonoDoCeu/`
- Builder: `Assets/_Project/Editor/Boss/DonoDoCeuBossBuilder.cs`

### Pose mapping

Read left to right, top to bottom. Give the slices stable descriptive names.

| Cell | Sheet A | Sheet B |
| --- | --- | --- |
| 1 | Grounded idle | Feather volley anticipation |
| 2 | Takeoff anticipation | Feather volley release |
| 3 | Hover upstroke | Swoop anticipation |
| 4 | Hover downstroke | Swoop attack |
| 5 | Whistle anticipation | Landing recovery |
| 6 | Whistle release | Stagger hit reaction |
| 7 | Tornado anticipation | Falling |
| 8 | Tornado release | Kneel hold |
| 9 | Tornado recovery | Kneel recovery |

Trash sheet is 2x2: top-left crushed red can; top-right white cup with red stripe; bottom-left crumpled paper; bottom-right crushed green bottle. Use all four as visual variations of the same crowd projectile behavior, rather than four separate mechanics.

Use `AttackVisualStep.BodySprite` / `BodySpriteFrames` and existing visual hooks where appropriate. Add minimal state/subphase presentation hooks only where existing steps cannot represent flight, falling, or recovery. Time critical pose changes to actual simulation beats, not unrelated view timers.

## Combat kit

Expose timings, ranges, counts, damage and movement as authored tuning. Values below are initial targets for playtesting, not established balance. Derive HP/damage baseline from existing bosses and preserve existing party/difficulty scaling.

### 1. Whistle — A Geral Não Perdoa

- Roughly 1 second windup: A5, chest inflated; clearly telegraph a forward cone, then lock aim before release.
- A6 at release. Only players actually hit receive a visible crowd-target mark and a short **movement slow**, initially 30% for 2 seconds. No stun/root, no direct whistle damage, no slow stacking or dash lockout.
- Existing `SlowEffectData` is Ice buildup and can trigger Freeze; do NOT use it for this non-elemental whistle. Inspect `StickySlowEffectData` / `StatusEffectUtility.ApplyStickySlow` and reuse the appropriate movement-slow semantics without unintended elemental buildup or changing existing effects.
- A hit triggers 2–3 short crowd-trash salvos against marked players. A miss produces no mark or trash attack. Keep a bounded TOTAL object budget; in co-op alternate marked targets rather than multiplying a full barrage per player. Handle dead/disconnected/invalid targets safely.
- Trash originates at valid WORLD-SPACE arena edges/lajes, never camera screen coordinates. Prefer existing arena bounds/markers; use a deterministic valid-ground fallback if no source positions are authored. No need for modeled crowd NPCs.
- Throw real ballistic/arc projectiles from elevated edge positions. Reuse `MortarBarrageDeliveryData`, `BallisticProjectileMovementData`, landing warnings and existing projectile damage infrastructure where suitable; a targeted delayed barrage may need a small reusable extension.
- Each throw samples its target position, shows a landing warning, then commits. No last-moment homing. Warnings must correspond to actual landing points, allowing a slowed player to walk out. Select can/cup/paper/bottle appearance deterministically and allow view-side spin.
- Keep damage modest and footprints small. Trash disappears shortly after impact; no persistent junk pile or extra slow puddle. Add whistle/crowd audio through existing hooks if suitable assets exist; document missing audio rather than inventing asset paths.

### 2. Vendaval da Geral — moving tornadoes

- A7 anticipation, A8 forceful release, A9 recovery. Spawn two tornadoes sequentially, with a hard cap of two active per boss, including across phases and cooldown resets.
- Tornadoes travel along readable, deterministic curved floor paths. No relentless target homing. Give them limited lifetime, a clear ground footprint, periodic damage to PLAYERS, and gentle inward pull that can be escaped by walking.
- At natural expiration, briefly signal compression, contract visually, and apply ONE final local pulse. This is compression-inspired, not Yoru's hard gravity hold. Keep tell duration, radius, pulse damage and pull strength tunable.
- Cancelled tornadoes (kneel, boss death, encounter teardown) dissipate harmlessly: NO final compression damage on cancellation.
- Reuse the EXISTING whirlwind particles. Inspect these candidates and create an owned variant of the best fit:
  - `Assets/3rd-party/Epic Toon FX/Prefabs/Environment/Weather/Whirlwinds/Whirlwind.prefab`
  - `Assets/3rd-party/Epic Toon FX/Prefabs/Environment/Weather/Whirlwinds/Whirlwind2.prefab`
  - `Assets/3rd-party/Epic Toon FX/Prefabs/Environment/Weather/Whirlwinds/Whirlwind3.prefab`
  - `Assets/3rd-party/Epic Toon FX/Prefabs/Environment/Weather/Whirlwinds/Whirlwind3 1.prefab`
- Restyle the variant as grey/black wind with restrained red accents, sparse red/black feathers and optional small Rift fragments using existing suitable assets. Keep opacity low enough to see players and warnings. Do not regenerate or replace the whirlwind from scratch or edit third-party originals.
- Inspect `Assets/_QuantumUser/Simulation/Systems/Combat/VortexSystem.cs`, `Assets/_QuantumUser/Simulation/Assets/Projectile/SpawnVortexEffectData.cs`, and Yoru Compression for reusable patterns. IMPORTANT: current vortex queries and implosion damage target ENEMIES and include hero-specific upgrades. Do not blindly attach a hero vortex to the boss or globally switch its target mask. Build/reuse a properly owner-attributed, player-targeting hazard with bounded movement/lifetime, no hero upgrade inheritance, and no regression to Yoru.

### 3. Rasante do Capitão — swoop

- B3 preparation in the air with a ground lane warning; lock direction before commitment. B4 during the dive/forward pass, B5 on landing/recovery.
- Real simulation movement through height and horizontal space. Do not implement the swoop as a visual sprite offset while damage remains elsewhere.
- Reuse charge/leap movement and line-warning conventions if appropriate. Audit `LeapDeliveryData` interruption/landing assumptions before using it for a continuously flying actor; introduce an opt-in reusable delivery if needed.
- Sweep damage along the committed path with at most one hit per player per pass. Validate arena walls, supported landing floors, gaps and allowable elevation changes. Land safely and give a meaningful grounded recovery window accessible to melee builds.

### 4. Penas de Rift — feather fan

- B1 windup, B2 release. A simple spaced fan of feather/crystal projectiles with generous dodge openings, real collision, wall blocking, and finite lifetime.
- Reuse `FanProjectileDeliveryData` / existing projectile assets as templates. Use an existing appropriate shard/feather visual or an approved-sheet-derived asset without altering source sheets; document any remaining visual placeholder.
- This is the basic ranged pressure attack between larger skills, not another major arena hazard.

## Flight, stagger and arena rules

This is a 2.5D TOP-DOWN game WITH REAL HEIGHT. Audit `EnemyHeightData`, flying movement, phase overrides, targeting and damage-height gates. Use per-entity state; never change a shared asset to temporarily ground one boss. Account for existing flight readers rather than assuming changing a new enum alone affects them all.

- Alternate readable airborne attacks and accessible ground windows. Hover can alternate A3/A4; takeoff uses A2. Existing height/weapon targeting conventions must remain consistent; no permanent invulnerability or melee-only party soft lock.
- Ground markers attach to actual floors. A tornado's tall visual column must not damage another floor merely sharing XZ. Use floor-height gating in addition to hit-radius tests. Ground-following hazards stop/dissipate at unsupported edges or blocked traversal rather than crossing voids.
- Stagger uses the current boss percentage thresholds and forced-action structure. Required sequence: B6 hit reaction -> B7 controlled fall if airborne -> B8 grounded kneel/exposure -> B9 rise -> normal behavior. If already grounded, omit the unnecessary airborne fall.
- Find a valid landing inside the arena; restore physics, gravity, collider and height state on completion/interruption/death. Exposure duration starts AFTER safe touchdown, not while still falling. Reuse Rupture/Kneel behavior for extra damage taken.
- Existing forced-action deferral protects active kinematic deliveries, but merely deferring an ordinary kneel until a swoop finishes is not sufficient for all flight states. Design safe pending-break/cancellation semantics; never teleport the boss into the floor or leave an active delivery orphaned. Inspect `BossSystem.TryForceAction`, `DeferForcedActionsWhileActive` and delivery cleanup before changing them.
- At stagger break cancel tornadoes, marks and future crowd throws. Already-thrown trash may finish its visible warned flight; cancel safely on encounter teardown. Restore player movement normally and preserve unrelated status effects.
- Start with a single major mechanic active at a time: suppress whistle/crowd while tornadoes are active and vice versa; reserve swoop for after those sequences. Feather pressure can accompany tornadoes only at a readable cadence. Do not create unavoidable slow + pull + overlapping trash damage.
- Use boss-owned simulation handles/tags for cleanup. Pause, death, defeat cinematic, despawn, retries and pooled views must not leave hazards, warnings, marks, VFX or audio loops behind.

## Phases and integration

Start with two HP phases, second at approximately 50%. Both use the same four mechanics; phase two may modestly shorten recovery or change the feather spread. Preserve telegraph/reaction time, tornado cap and crowd budget. No extra summons, poleirо destruction mechanic or additional attack is required.

Author forced kneel outside normal action selection. Keep basic/skill slot numbering consistent with `EnemyDecisionUtility` and document it. Use existing boss introduction/defeat/HUD, no separate UI system. Use a suitable approved pose for reveal art if a dedicated portrait is unavailable.

Expose the real prototype through existing `DebugPrototype`, Spawn Near Local Player and Jump To This Boss / debug override flows. At inspection time only W1/W2 world definitions exist: check current state, wire into Favela only if it already has a real config, otherwise provide the normal debug encounter route without inventing or replacing world progression.

## Deliverables and validation

Deliver builder, prepared/sliced sprite assets, view/entity/projectile/hazard prefabs, Quantum data assets, necessary deterministic components/systems/deliveries, and `docs/dono-do-ceu-boss.md` describing tuning, pose bindings, debug entry, lifecycle and limitations. Generate QTN output using the project's established codegen workflow; do not hand-edit generated code.

Compile and use available Unity verification tools. Validate meaningful cases: single/multiple marked players, whistle miss/no Freeze, committed trash warnings, two-tornado cap, player-only damage, height separation, safe swoop landing, airborne and grounded stagger, no compression burst on cancellation, exposure starting after landing, phase transitions, boss death cleanup and builder rerun identifier stability. Check that W1/W2/Riff Riot/Yoru retain behavior. Run actual encounter checks where possible and explicitly distinguish verified behavior from pending editor/online/co-op playtests. Report final paths and material limitations; do not call the boss complete with unconnected assets or unreported placeholders.
