# Enemies

See [architecture.md](architecture.md) for the Simulation/View split this doc assumes.

## How an enemy is put together

One AI system (`EnemySystem`) and one component (`Enemy`) drive every enemy in the game. A new
enemy *type* (reusing an existing action) is data + a prefab, not new code; a new *delivery* type
(how an action actually connects — melee swing, projectile, dash, ...) is one new self-contained
`EnemyDeliveryData` subclass, with zero changes to `EnemySystem` itself — see
[Adding a new delivery type](#adding-a-new-delivery-type) below. Each concrete enemy is:

| Layer | Asset | Role |
|---|---|---|
| Simulation | `EnemyDataAsset` (`Simulation/Assets/Enemy/EnemyDataAsset.cs`) | Stats: move speed, tier, height/traversal data (`EnemyHeightData`), traits (`EnemyTrait[]`), detection/leash range, death linger time, a movement policy (`AssetRef<EnemyMovementData>`), a targeting policy (`AssetRef<EnemyTargetingData>`), a `BasicAction` (`AssetRef<EnemyActionData>`, always present) and optional `SkillActions` (`List<AssetRef<EnemyActionData>>`, up to 7) chosen between by `EnemyDecisionUtility`. |
| Simulation | `EnemyActionData` (`Simulation/Assets/Enemy/Actions/`) | One enemy's one action's shared tuning: `EngageRange`/`DamageRange`/`Damage`/`Knockback`/`AnticipationTime`/`DirectionTracking`/`AimLock`/`IgnoreY`/`DownTime`/`CooldownTime`/`TelegraphStartPercent`, plus an `AssetRef<EnemyDeliveryData> Delivery` pointing at whichever concrete delivery actually executes it. `EngageRange` is how close the target must be to *trigger* the action (Chasing → Preparation); `DamageRange` is how close it must be to actually *connect* — usually the same distance for instant deliveries (melee/projectile), but Charge needs `EngageRange` set well beyond `DamageRange` since it has to close a gap before it can connect. For an action whose Telegraph is a Circle/Cone, `DamageRange` also drives the decal's actual radius (`damageRange * TelegraphData.RadiusMultiplier`, resolved in `EnemyAttackVisualsView.ComputeTelegraphPose`) - see `EnemyActionData.DamageRange`'s own comment. No longer itself polymorphic — that's `EnemyDeliveryData`'s job, so the same tuning can pair with any delivery type. `DirectionTracking == DoNotUpdateTargetDirection` locks `Enemy.SkillTargetPosition`/`Aim` the instant Preparation begins (Charge/Leap use this); any other `DirectionTracking` value keeps re-aiming through the windup until `AimLock` says to stop — `LocksAtTelegraphEnd` (default, matches this system's original behavior) keeps tracking all the way to the swing itself, `LocksAtTelegraphStart` freezes the instant the windup becomes visible as a Telegraph, `LocksAtAnticipationStart` freezes immediately (same effect as `DoNotUpdateTargetDirection`, expressed here instead). See `EnemyDeliveryData.OnAnticipating`. |
| Simulation | `EnemyDeliveryData` (`Simulation/Assets/Enemy/Actions/Delivery/`) | Owns the actual execution logic (`Begin`/`Tick`) for one delivery style — `MeleeAreaDeliveryData`, `ProjectileDeliveryData`, `ChargeDeliveryData`, `LeapDeliveryData`, ... `EnemySystem` never branches on delivery type, only calls `Begin`/`Tick` and reacts to the returned bool. Its own reusable Quantum asset (`AssetRef<EnemyDeliveryData>` on `EnemyActionData`), so the same tuned delivery can be shared across multiple actions/enemies. |
| Simulation | `EnemyMovementData` / `EnemyTargetingData` (`Simulation/Assets/Enemy/Movement/`, `.../Targeting/`) | Composable movement direction and target-selection policies — `ChaseMovementData`, `MaintainDistanceMovementData`, `OrbitMovementData`, `FleeMovementData`, `StationaryMovementData`; `NearestPlayerTargetingData`, `CurrentTargetLockTargetingData`, `RandomPlayerTargetingData`, `MostIsolatedPlayerTargetingData`, `LargestPlayerClusterTargetingData`, `LowestHealthAllyTargetingData`, `HighestHealthAllyInRangeTargetingData`. Decoy priority ("max aggro") is NOT one of these — `EnemySystem` applies it as an override on top of whichever targeting policy is active. |
| Simulation | `Enemy` component (`QTN/Enemy/Enemy.qtn`) | Runtime state: which `EnemyDataAsset` it uses, current target, AI phase (`EnemyActionPhase`), phase timer, attack cooldown remaining, captured skill-target position (for deliveries like Charge that need one). |
| Simulation | `EnemySystem` (`Systems/Enemy/EnemySystem.cs`) | The one AI state machine every enemy runs — see below. Generic over whatever `EnemyDataAsset`/`EnemyActionData`/`EnemyDeliveryData` the entity points at. |
| Simulation | `EnemyMovementUtility` (`Systems/Enemy/EnemyMovementUtility.cs`) | Shared movement/query helpers (stop, move in a direction, face target, find nearest player, resolve a target's position, ledge/ground-ahead checks) — used by `EnemySystem`, every `EnemyMovementData`/`EnemyTargetingData`/`EnemyDeliveryData` subclass, so none of them need to reach back into the system that owns them. |
| Simulation (View-only fields) | `EnemyActionData.View.cs` / `AttackVisualStep.cs` / `TelegraphData.cs` (`Simulation/Assets/Enemy/Actions/`) | `EnemyActionData` is `partial` — its simulation-relevant fields/logic live in `EnemyActionData.cs`, while `AnticipationStep`/`BeginStep`/`OnGoingStep`/`EndStep` (each an `AttackVisualStep`: body-animation type + optional particle) and an optional `Telegraph` (`AssetRef<TelegraphData>`: ground line/area indicator spanning any two phase edges, its own shared/reusable asset — see below) live in the companion `EnemyActionData.View.cs` file. Built-in Unity types (`ParticleSystem`, `GameObject`) compile fine directly on a Simulation-assembly class — the hard wall is only against referencing the View project's *own* custom classes (would be a circular assembly reference, since `Quantum.Unity` already depends on `Quantum.Simulation`) — see [architecture.md](architecture.md). One shared `AttackVisualStep`/`TelegraphData` shape, not per-subclass files — every concrete delivery/action pairing gets full visual configurability with no C# subclassing. `TelegraphShape` declares the full intended vocabulary (Circle, Cone, Rectangle, AimLine, LandingMarker, ChargeLane, ProjectilePath, EnemyPose, SoundCue, HeightShadow, CountdownFill) but only `Circle`/`Cone`/`ChargeLane`/`Rectangle` actually render anything today (see `EnemyAttackVisualsView.SpawnTelegraph`) — the rest are declared for schema completeness, picking one is a documented no-op until its own rendering path exists. |
| Both | `*EntityPrototype.qprototype` + matching `.prefab` under `Assets/QuantumUser/Entities/Enemies/` | The prefab **is** the Quantum entity prototype — it carries the `Enemy` component override (which `EnemyDataAsset` to use) *and* is the thing instantiated as the view. One prefab = one enemy type, fully self-contained. |
| View | `EnemyView` | Hit flash, death tint, shadow toggle, HUD widget lifecycle (`EnemyUiWidgetManager`). Shared code — no per-type overrides needed. |
| View | `EnemyBlobAnimationView` | Procedural squash/stretch animation for **idle/run/die only** — attack-phase animation was pulled out (see below), since it's per-*action*, not per-*enemy-prefab*. Exposes `PlayAttackStep(AttackVisualStep)`, which drives the same squash/lean/rock/bob math off whatever step it's given, for `EnemyAttackVisualsView` to call. |
| View | `EnemyAttackVisualsView` | Reads `Enemy.Phase` edges (Preparation entry / Begin / Active entry / End — see the state machine below) each frame, resolves the enemy's `EnemyActionData` fresh off the frame, and for each phase: calls `EnemyBlobAnimationView.PlayAttackStep` for its `AttackVisualStep`, spawns its particle (`EffectsManager` for one-shot, a tracked `Instantiate` for `Parented` ones), and shows/hides the `TelegraphData` across its configured `StartPhase`→`EndPhase` window. Owns *when*/*which*; `EnemyBlobAnimationView` still owns *how* to render a step. Also subscribes to `EventProjectileDestroyed` (filtered by `e.Owner == _entityRef`) and plays `ProjectileDataAsset.DestroyEffectPrefab` (resolved via the event's `ProjectileData` ref) at the real destroy position (`e.Position`) — data-driven rather than per-prefab like `WeaponView.projectileDestroyEffectPrefab` for player weapons, so it's authored once per `ProjectileDataAsset` and automatically shared by every enemy that fires it. |

### AI state machine (`EnemySystem`)

```
Idle --(target within DetectionRange, via decoy priority or EnemyTargetingData.SelectTarget)--> Chasing
Chasing --(target lost)--> Idle
Chasing --(target beyond LeashRange)--> Idle
Chasing --(target within Action.EngageRange AND AttackCooldownRemaining <= 0)--> Preparation
Preparation --(elapsed windup crosses Action.TelegraphStartPercent)--> Telegraph
    (both phases share one StateTimer/AnticipationTime - Telegraph is unreached today since
    TelegraphStartPercent defaults to 1)
Preparation/Telegraph --(StateTimer, = Action.AnticipationTime, elapses)--> Delivery.Begin(...)
    Begin resolves instantly (melee/projectile)      --> Recovery
    Begin needs more ticks (e.g. Charge's dash)       --> Active
Active --(every tick: Delivery.Tick(...), until it reports done)--> Recovery
Recovery --(StateTimer, = Action.DownTime, elapses, target still in LeashRange)--> Chasing
Recovery --(target lost / out of LeashRange)--> Idle
(any, via DamageUtility when Health hits 0)--> Dead  [terminal; corpse lingers DeathLingerTime, then destroyed]
```

`Enemy.Phase` (`EnemyActionPhase`) merges what used to be two separate concepts - a coarse AI
state and a finer windup/telegraph/active-delivery phase - into one state machine at a single
resolution, since they were never orthogonal. Cooldown is deliberately *not* one of its values:
`Enemy.AttackCooldownRemaining` already ticks independently of `Phase` every tick
(`EnemySystem.TickAttackCooldown`), which is what lets an enemy resume `Chasing` while its one
action is still cooling down.

Movement while `Chasing` is resolved by whichever `EnemyMovementData` the enemy's `EnemyDataAsset`
points at (`ComputeMoveDirection`), then applied via `EnemyMovementUtility.MoveInDirection` — which
also enforces `EnemyHeightData.AvoidLedges` (refuses to step off a ledge with no ground ahead,
mirroring the player's own auto-hop ground check, just without the hop). Target acquisition (Idle →
Chasing) checks decoy priority first, then falls back to whichever `EnemyTargetingData` is
configured — see the table above.

Every enemy has a `BasicAction` (`EnemyDataAsset.BasicAction`) and optionally more `SkillActions` -
`EnemyDecisionUtility.TrySelectAction` scores every eligible one (off cooldown, target within
`EngageRange`, `Trigger` condition met) on the `Chasing` → `Preparation` transition and picks the
highest (`SelectionWeight + RangeScore + TargetCountScore − RepetitionPenalty`; `PositionScore` is a
placeholder until a line-of-sight concept exists), recording which slot won on
`Enemy.CurrentActionSlot` so every later phase handler resolves the same action. With no
`SkillActions` configured (the common case) this reduces deterministically to "is `BasicAction` off
cooldown and in range" - the original single-action gate's exact outcome. `EnemySystem` never knows
which concrete delivery type is active: it calls `EnemyDeliveryData.Begin(...)` once the windup
ends, and — only if that returned `false` — `EnemyDeliveryData.Tick(...)` every tick after that
until told the delivery is finished. This is also why a new delivery type never touches
`EnemySystem.cs`.

**Per-slot cooldowns**: `BasicAction`'s cooldown lives directly on `Enemy.AttackCooldownRemaining`
(every enemy pays for this either way). `SkillActions`' cooldowns live on a separate, *optional*
`EnemyActionSlots` component (`array<FP>[7] SkillCooldowns`) that only needs adding to a prototype
whose `EnemyDataAsset.SkillActions` is actually non-empty — a filler enemy with only `BasicAction`
never carries it, so it pays zero extra memory for a feature it doesn't use. Max 7 skill actions
per enemy (`EnemyActionSlots.SkillCooldowns`'s fixed size).

`DownTime` (stationary recovery, drives `Recovery`'s duration — no movement, no re-facing) and
`CooldownTime` (how long until the action is usable again, tracked per-slot as above) are separate
fields on `EnemyActionData`. An action whose `CooldownTime` is longer than its `DownTime` lets the
enemy resume chasing before that same action is available again.

**Interrupt**: a knockback lands (`OnEnemyKnockedBack`) only if `EnemyDataAsset
.CanBeInterruptedByKnockback` is true (else the enemy is fully immovable, no stagger window ever
opens). Given that, whether it *also* cancels the in-progress action is a **per-action** choice -
`EnemyActionData.InterruptibleDuringTelegraph` (default true) for a mid-windup cancel,
`InterruptibleDuringActive` (default false) for a mid-delivery one. A cancelled windup never called
`Begin()`, so there's nothing to clean up; a cancelled Active delivery gets one call to
`EnemyDeliveryData.OnInterrupted(...)` first (default no-op) so it can leave things tidy - though
today's kinematic deliveries (Charge/Leap) can never actually reach that path, since
`DamageUtility.ApplyResolvedImpulse` skips a kinematic `PhysicsBody3D` entirely (no impulse lands,
so the signal never fires while they're Active).

`EnemyHeightData.InitialState == Flying` enemies ignore gravity and chase at `FlightHeight` above
the target (horizontal direction only today — no active altitude-seek yet, see
`EnemyMovementUtility.MoveInDirection`); `Grounded` enemies only steer horizontally and fall
off ledges like anything else on `PhysicsBody3D`, unless `AvoidLedges` stops them at the edge first.

## Roster

| Name | `EnemyDataAsset` | Prefab | Notes |
|---|---|---|---|
| Brute | `Assets/QuantumUser/Resources/Enemy/EnemyDataAsset.asset` | `Assets/QuantumUser/Entities/Enemies/BasicEnemy.prefab` | The only enemy in the game today. Melee (`MeleeAreaDeliveryData`), grounded. Placed as a static instance in `QuantumUser/Scenes/QuantumGameScene.unity` (no runtime spawner exists yet). |

## Planned roster

| Name | Delivery shape | Status |
|---|---|---|
| Melee (Brute) | Melee | Shipped — see Roster above. |
| Ranged | Projectile, straight-line | Fits today's schema as-is — `ProjectileDeliveryData` already exists (`UseArc = false`). Just needs a data asset + prefab, following the checklist below. |
| Rusher | Dash-strike | **Sim implemented** (`ChargeDeliveryData`) — locked-direction charge captured at windup-end, hit-check each tick, whiffs on arrival/timeout/wall (`EnemyMovementUtility.IsBlockedByWall`, static geometry only). Just needs a data asset + prefab, following the checklist below, same as Ranged. |
| Bomber (mortar) | Projectile, lobbed/arc | **Sim implemented** — `ProjectileDeliveryData.UseArc = true` (+ `LaunchAngle`/`Gravity`) reuses `ProjectileSpawner.SolveArcLaunch`, no separate delivery class. Just needs a data asset + prefab, following the checklist below, same as Ranged. |
| Tanker | Frontal shield (blocks damage from the front arc) | `EnemyTrait.FrontalDamageReduction` exists as a flat flag (see `EnemyDataAsset.Traits`) but has no consumer wired yet — see below. |

## Delivery types

All under `Simulation/Assets/Enemy/Actions/Delivery/`: `MeleeAreaDeliveryData`, `ProjectileDeliveryData`
(straight-line or, with `UseArc = true`, lobbed; `WaitForImpact = true` hands off to the spawned
shot via `Enemy.SkillProjectile` and keeps the enemy in `EnemyActionPhase.Active` — and its
`Telegraph`, if `EndPhase = Destroyed`, visible — for the shot's whole flight instead of resolving
instantly on throw), `ChargeDeliveryData`, `LeapDeliveryData`,
`GroundAreaDeliveryData` (instant AoE at the locked anchor, no movement), `BeamDeliveryData`
(channeled directional box sweep, ticks every `TickInterval`), `AuraDeliveryData` (channeled
self-centered radius pulse, same tick shape as Beam), `ScatterDeliveryData` (resolves `Count`
random points around the locked anchor and runs `action.Effects` at each with `Target =
EntityRef.None` — only chooses where; a `SpawnEntityEffectData` in `Effects` is what actually drops
a prototype via the shared `SpawnedEntitySpawner`, same effect projectile impacts use), `PullGrabDeliveryData` (drags the target in via
`DamageUtility.ApplyPull`, hits it once the pull finishes), `TeleportBlinkDeliveryData` (instant
reposition, no `Effects` of its own — chain into a `SequenceDeliveryData` for "blink then strike").
`SequenceDeliveryData` (an ordered `Steps` list of sub-actions, each with its own `Delivery`) runs
one after another via the sub-step's own `Begin`/`Tick` — a step's `AnticipationTime`/`Telegraph`
aren't used (the outer action's one windup already telegraphed the whole sequence). Requires the
optional `EnemySequenceState` component (tracks the current step index) on any prototype that uses
it, same opt-in reasoning as `EnemyActionSlots`.

## Extending the schema for planned types

### Tanker (frontal shield)

`EnemyTrait.FrontalDamageReduction` (see `EnemyDataAsset.Traits`) is the placeholder for this — no
consumer wired yet. Two directions, same tradeoff as originally scoped:

- **(a) Facing-based damage reduction, no new collider.** Check the attacker's position against the
  defender's `Aim.Angle` in `DamageUtility.ApplyDamage` when the defender `Has<Enemy>` and its
  `EnemyDataAsset.Traits` contains `FrontalDamageReduction`, reducing/ignoring damage inside a
  configurable arc. **Blast radius note:** `DamageUtility.ApplyDamage` is the single shared entry
  point for *all* damage in the game (player and enemy dealt) — any change there has to default to
  "no reduction" for every entity that doesn't opt in, and needs care not to slow down the common case.
- **(b) A literal shield collider.** A distinct child object/component (its own small `.qtn`
  component, `Shield`, with position/arc and maybe its own hit points) parented in front of the
  enemy, blocking incoming melee/projectile hits from that side. Bigger lift: new component, new
  collision-layer/query logic, and a View-side prefab child to match.

(a) is the cheaper build and reuses existing facing (`Aim.Angle`) already computed every tick; (b)
is more truthful to "shield" as a real in-world object a player could flank or destroy.

## Bosses

`BossDataAsset : EnemyDataAsset` (`Simulation/Assets/Enemy/Boss/BossDataAsset.cs`) adds:

- `Phases` (`List<BossPhaseData>`) — index 0 is the base phase (always active at spawn); each
  later one has an `EntryTrigger` (`HealthThreshold`/`Timer` are actually polled every tick by
  `BossSystem`; `ArenaEvent`/`AddWaveCleared`/`Scripted` are declared but have no existing hook into
  this codebase's game state yet — see that enum's own comment), an `ActionPoolSlots` (indices into
  the inherited `SkillActions` eligible during that phase), and a `MovementOverride`/`HeightOverride`
  (not yet read anywhere — see below) plus `Modifiers` (`BossStatModifiers`: MoveSpeed/Damage/
  DamageTaken multipliers, also not yet consumed — applying them would mean scaling reads at each
  point of use, e.g. `EnemySystem`'s `moveSpeed` line, since `EnemyDataAsset` is a shared, immutable
  asset that can't just be mutated per-instance).
- `GlobalActionSlots` — `SkillActions` indices eligible in every phase (a "panic button" pool).
- `Stagger` (`StaggerProfileData`: `Threshold`/`RegenRate`/`OnBreakForcedAction`) — optional
  (`Threshold <= 0` disables it entirely).

`BossRuntimeState` (component, opt-in — only add to a boss entity's prototype, same reasoning as
`EnemyActionSlots`/`EnemySequenceState`) tracks `CurrentPhaseIndex`, `PhaseTimer`, and
`StaggerMeter`/`LastObservedHealth`. `BossSystem` (registered right after `EnemySystem` in
`SystemSetup.User.cs`) drives phase advancement and stagger: `StaggerMeter` builds from damage
taken (diffed off `Health.CurrentHealth` each tick — no dedicated on-damaged signal exists, so this
avoids touching `DamageUtility`'s shared pipeline) and drains at `RegenRate` otherwise; crossing
`Threshold` hard-forces `OnBreakForcedAction` (sets `Enemy.Phase`/`CurrentActionSlot` directly,
without calling the interrupted delivery's `OnInterrupted` — a stagger break is a guaranteed
scripted moment, not a conditional interrupt; known gap if a boss is mid-delivery when it triggers,
not yet exercised by any real boss).

`EnemyDecisionUtility.TrySelectAction` is boss-aware: a skill slot not in `GlobalActionSlots` or the
current phase's `ActionPoolSlots` is filtered out entirely (hard gate, not just a lower score), and
`PhaseScore` (a small bonus for an action that's specifically part of the current phase's own pool,
vs. merely a `GlobalActionSlots` pick) fills what was previously an unused scorer term.

**What already carries over regardless**: bigger stats are just bigger numbers on the same fields —
`Health.MaxHealth`/`Armor` (a plain component override on the prototype, not tied to any data asset
— see `Health.qtn`) and `EnemyDataAsset`'s `MoveSpeed`/`DetectionRange`/`LeashRange`/`SkillActions`,
tuned up same as any enemy.

## Adding a new enemy type

1. **Get an action asset.** Reuse an existing `EnemyActionData` asset (point at the same one
   another enemy already uses, say) or create a new one under `Simulation/Assets/Enemy/Actions/`
   (`Create → Quantum → ...`), and tune its `EngageRange`/`DamageRange`/`Damage`/`AnticipationTime`/
   `DownTime`/`CooldownTime`. Point its `Delivery` field at whichever concrete `EnemyDeliveryData`
   (`MeleeAreaDeliveryData`/`ProjectileDeliveryData`/`ChargeDeliveryData`/`LeapDeliveryData`, under
   `Simulation/Assets/Enemy/Actions/Delivery/`) matches the shape, tuning its own delivery-specific
   fields there. For instant-hit deliveries, `EngageRange` is usually left equal to `DamageRange`;
   for a charge-style delivery, `EngageRange` needs to be set well beyond `DamageRange` (and `DashDistance`
   needs to be able to cover at least that gap) or the action triggers already within its own
   connect distance and resolves instantly instead of visibly happening.
2. **Duplicate the data asset.** Right-click an existing `EnemyDataAsset.asset` → Duplicate (or
   `Create → Quantum → ...` if starting fresh). Set `MoveSpeed`, `Height`, `Movement`, `Targeting`,
   `DetectionRange`, `LeashRange`, `DeathLingerTime`, and point `BasicAction` at the asset from step
   1 (leave `SkillActions` empty for a simple single-action enemy - no `EnemyActionSlots` component
   needed on the prefab either in that case). Give it a clear name and drop it under
   `QuantumUser/Resources/Enemy/`.
3. **Duplicate the prefab.** Copy `BasicEnemy.prefab` (`Assets/QuantumUser/Entities/Enemies/`). On
   the `QPrototypeEnemy` component, point `EnemyData` at the new data asset from step 2.
4. **Retune the view.** On the copied prefab: swap the sprite/art on `EnemyView`'s `bodySprite`,
   adjust `hitFlashColor`/`deathColor` if desired. Action-phase tells (windup shake, strike pose,
   particles, ground telegraph) are **not** set per-prefab anymore — they live on the
   `EnemyActionData` asset from step 1 and apply automatically to every enemy using it (see
   [Adding a new delivery type](#adding-a-new-delivery-type) below for where to tune those). Use
   `EnemyView`'s `Flash` button and `EnemyBlobAnimationView`'s `TriggerDie`/`PlayDebugTestStep`
   buttons to preview poses in the Inspector without running the sim.
5. **Duplicate the `.qprototype`.** Quantum should generate/link one automatically alongside the
   prefab when you save it as a new entity prototype — confirm it's a distinct asset from
   `BasicEnemyEntityPrototype.qprototype`, not still pointing at the same one.
6. **Place it.** Drag the new prefab into a scene (currently the only spawn mechanism — see
   Roster note above).
7. **Add a row to the Roster table above.**

No `EnemySystem`/`Enemy.qtn` changes needed for a new enemy as long as it fits the existing
Idle/Chasing/Preparation/Telegraph/{Recovery|Active}/Dead shape and composes existing
`EnemyMovementData`/`EnemyTargetingData`/`EnemyDeliveryData` types — see
[architecture.md](architecture.md) for why enemies don't need a view-catalog bridge the way weapons do.

## Adding a new delivery type

Add one new subclass under `Simulation/Assets/Enemy/Actions/Delivery/` (`: EnemyDeliveryData`),
following `MeleeAreaDeliveryData`/`ProjectileDeliveryData`/`ChargeDeliveryData`/`LeapDeliveryData`
as examples:

1. Add whatever type-specific fields it needs (the shared `DamageRange`/`Damage`/`Knockback`/
   `AnticipationTime`/`DownTime`/`CooldownTime`/`TelegraphStartPercent` already live on the paired
   `EnemyActionData`, not here).
2. Implement `Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData
   action, EntityRef target)` — called once when the windup ends. Return `true` if the action fully
   resolves this same tick (melee/projectile-style); return `false` if it needs to keep running
   over multiple ticks (a dash, a jump-and-slam, ...), in which case `EnemySystem` switches the
   enemy into `EnemyActionPhase.Active` and starts calling `Tick` every tick after.
3. If `Begin` can return `false`, also override `Tick(Frame f, ref EnemySystem.Filter filter,
   EnemyDataAsset data, EnemyActionData action, EntityRef target)` — called every tick until it
   returns `true`.
4. Use `EnemyMovementUtility` for movement/queries (`MoveInDirection`, `MoveKinematicTowards`,
   `StopMovement`, `FaceTarget`, `TryFindNearestPlayer`, `TryGetTargetPosition`,
   `ResolveDestination`, `FlatSqrDistance`, `IsBlockedByWall`) instead of reaching into
   `EnemySystem` — delivery types are meant to be self-contained. `Enemy.SkillTargetPosition` is
   available on the `Filter` for deliveries that need to capture a locked point (see
   `ChargeDeliveryData`).
5. Create a `.asset` instance of the new type (`Create → Quantum → ...`, same as any other data
   asset) and point an `EnemyActionData.Delivery` at it. No new C# needed for the tell — every
   `EnemyActionData` already has `AnticipationStep`/`BeginStep`/`OnGoingStep`/`EndStep`
   (`AttackVisualStep`: pick an `AttackAnimationType` + tune whichever fields the Inspector shows
   for it, plus an optional particle) and an optional `Telegraph` (`AssetRef<TelegraphData>`: a
   ground line/area indicator spanning any two phase edges, e.g. `Anticipation`→`Begin` for a
   charge-up warning line — reuse an existing `TelegraphData` asset or create a new one). Set
   these directly on the action asset — `EnemyBlobAnimationView`/`EnemyAttackVisualsView` pick them
   up automatically, no enemy-prefab-side configuration needed.

No `EnemySystem.cs` changes are needed for any of this — that's the point of the architecture.
