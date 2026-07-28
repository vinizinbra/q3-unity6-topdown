# Character Skills

See [architecture.md](architecture.md) for the Simulation/View split this doc assumes, and
[enemies.md](enemies.md) for `EnemyDeliveryData` — the enemy-side polymorphism `SkillData` is the
direct player-side analog of.

## How a skill is put together

One system (`SkillSystem`) and one component (`CharacterSkills`) drive both of a player's skill
slots. A new skill *type* is one new self-contained `SkillData` subclass, with zero changes to
`SkillSystem` itself. A new *behavior* (spawn something, explode, knock back, grant invulnerability,
...) that should be mixable onto any skill is one new `SkillActionData` subclass, also with zero
`SkillSystem` changes — see [Adding a new skill type](#adding-a-new-skill-type) and
[Adding a new composable action](#adding-a-new-composable-action) below.

| Layer | Asset | Role |
|---|---|---|
| Simulation | `SkillData` (`Simulation/Assets/Skills/`) | One skill's execution logic: `abstract bool Begin(...)` / `virtual bool Tick(...)` / `virtual void End(...)`, plus `MaxStacks` (seed value, see below), `RechargeTime`, and an `Actions` list. Each concrete type (`DashSkillData`, ...) owns its own movement/effect logic — `SkillSystem` never branches on skill type. One asset **per level** — leveling up is swapping which `AssetRef<SkillData>` a slot points at, not a per-level array on one asset. |
| Simulation | `SkillActionData` (`Simulation/Assets/Skills/`) | One composable behavior: a single `abstract void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)` plus a `[Flags] Phase` field (`Begin`/`OnGoing`/`End`, combinable). **When** an action fires is Inspector-configurable data, not which method got overridden — see [Composable actions](#composable-actions) below. Referenced from `SkillData.Actions` (one list, not three separate per-phase lists). |
| Simulation | `CharacterSkills` component (`QTN/CharacterSkills.qtn`) | Two fixed, named slots (`DashSkill` → Shift, `HeroSkill` → E — not an array, since which slot is which is a hardcoded design constant). Each `SkillSlot` holds which `SkillData` it points at, `SkillState` (`Ready`/`Active`), `MaxStacks`/`CurrentStacks`/`RechargeTimer` (see [Charges, not a cooldown lock](#charges-not-a-cooldown-lock)), captured `StartPosition`/`TargetPosition`, and `SpawnedEntity` (e.g. a spawned decoy, so a later activation of the same slot can find/replace it). |
| Simulation | `SkillSystem` (`Systems/SkillSystem.cs`) | The state machine every skill slot runs — see below. Generic over whatever `SkillData`/`SkillActionData` a slot points at; only calls `Begin`/`Tick`/`End` and `SkillActionData.Execute`, reacting to returned bools and each action's own `Phase`. |
| Simulation | `Decoy` component + `DecoySystem` (`QTN/Decoy.qtn`, `Systems/DecoySystem.cs`) | Tag + lifespan for an entity spawned by `SpawnDecoySkillAction` to pull enemy targeting — see [Decoy "max aggro"](#decoy-max-aggro) below. |
| Simulation | `ExplosionUtility` (`Systems/ExplosionUtility.cs`) | Shared AoE damage+knockback sweep, used by projectile area impacts and `ExplodeSkillAction` — one `OverlapShape` loop, not duplicated per caller. |

### State machine (`SkillSystem`)

```
Ready --(button WasPressed AND CurrentStacks > 0)--> CurrentStacks--, Skill.Begin(...) + Actions(Begin)
    Begin resolves instantly                     --> Skill.End(...) + Actions(End) --> Ready
    Begin needs more ticks (e.g. Dash)            --> Active
Active --(every tick: Actions(OnGoing), then Skill.Tick(...), until it reports done)--> Skill.End(...) + Actions(End) --> Ready
(every tick, regardless of state, while CurrentStacks < MaxStacks)--> RechargeTimer counts down --> CurrentStacks++
```

`Actions(Begin)`/`Actions(OnGoing)`/`Actions(End)` means: iterate `SkillData.Actions`, and for each
one whose `Phase` includes that bit, call `Execute` with exactly that phase — see
[Composable actions](#composable-actions).

Unlike `EnemySystem`'s `Anticipating`/`Recovering`, there's no windup or forced-stationary recovery
beat — a skill fires on the input edge and is immediately re-usable from `Ready` the instant it ends,
*if* another stack is already banked. Availability is entirely governed by `CurrentStacks`, not a
timer-blocked third state.

### Charges, not a cooldown lock

A slot can bank up to `MaxStacks` uses; each spent stack recharges independently on its own
`RechargeTimer` — spending a stack starts a regen countdown immediately (unless one is already
running for an earlier-spent stack), and completing one immediately starts the next if still below
max. Standard charge-ability model: a slot at 0/3 stacks becomes usable again one stack at a time,
not all-or-nothing.

**`MaxStacks` is entirely component-owned — `SkillData` has no `MaxStacks` field at all.** It's
baked directly on the entity prototype's `CharacterSkills` component override, the same convention
as `Health.MaxHealth`/`Armor` (a plain per-prototype value, not tied to any data asset). This is
deliberate: a runtime upgrade (a "+1 charge" perk, say) needs to raise a slot's cap independently of
which `SkillData`/level is currently equipped, and reassigning a slot's `Skill` must never reset it
back down to whatever the newly-assigned asset "expects." `0` means "never baked" —
`PlayerSpawnUtility.InitSkillSlot` defaults a freshly-assigned slot still at `0` to `1`, so a
forgotten prototype override doesn't silently ship a permanently unusable (0-charge) skill.

**`SkillData.InitStacks` is a separate, asset-owned concept: how many stacks a slot starts with**
the moment this particular skill/level is first assigned to it — not the ceiling, just the starting
fill level, clamped to the slot's `MaxStacks` so a misconfigured `InitStacks` can never exceed the
slot's actual cap. This can reasonably differ per skill/level (e.g. a higher-level Dash asset might
start partway banked instead of empty) independently of how many charges the slot can ultimately
hold.

### Composable actions

`SkillActionData.Execute` is the one method every action implements. **When** it fires is
configurable data (`Phase`), not which method got overridden — `SkillSystem` invokes `Execute` once
per lifecycle point with exactly one `SkillActionPhase` bit set (`Begin`, `OnGoing`, or `End`); an
action whose `Phase` doesn't include that bit is skipped that tick.

This means "explode on `End`" (dash-end nova, the shipped default on `ExplodeSkillAction`) and
"explode on `Begin`" (nova on cast instead) are the **same class** with a different `Phase` value on
the asset instance — retargeting when a behavior fires is a one-field Inspector edit, never new C#.

Combine flags (`Begin | End`) for an action whose single `Execute` needs to run paired logic at both
ends — `InvulnerabilitySkillAction` is the example: increments `Health.InvulnerabilityStacks` when
called with `firedPhase == Begin`, decrements when called with `firedPhase == End`. This pairing is
also why `SkillData.Actions` is one list, not three separate per-phase lists: a paired action lives
in exactly one list entry, so it can't be added for one phase and forgotten for the other.

### Decoy "max aggro"

No general aggro/threat system exists — `EnemySystem` targeting is whatever `EnemyTargetingData`
policy is configured (nearest-distance via `NearestPlayerTargetingData` for the shipped roster)
with one override: `EnemyMovementUtility.TryFindNearestDecoy` makes any `Decoy` in range always win
regardless of policy, for both fresh target acquisition (`UpdateIdle`) and an already-`Chasing`
enemy (checked every tick, so a decoy pulls aggro even
mid-chase — but not mid-`Preparation`/`Telegraph`/`Active`, an enemy already committed to a
windup/action doesn't retarget). A `Decoy`'s collider sits on the **Player** physics layer, not a
new one — enemy delivery hit-*connect* checks (`MeleeAreaDeliveryData`/`ChargeDeliveryData`)
re-query "nearest thing on the
Player layer" independently of `Enemy.Target`, so a decoy that isn't on that layer would pull
targeting but then get whiffed on every attempt to actually hit it.

### Invulnerability and projectile pass-through

`InvulnerabilitySkillAction` is a reusable composable action (`Phase = Begin | End` by default —
not a hardcoded field on `SkillData`) that increments/decrements `Health.InvulnerabilityStacks` (a
counter, so overlapping invulnerability sources don't clobber each other's on/off).
`DamageUtility.ApplyDamage` early-returns while any stacks are present — damage-immunity only, not
knockback-immunity.

Damage-gating alone isn't enough for a *projectile*: without more, `ProjectileSystem` still resolves
a hit and destroys the projectile on contact, just for zero damage — reading as the player's body
blocking the shot, not dodging through it. On the `0 → 1`/`1 → 0` `InvulnerabilityStacks` transition,
`InvulnerabilitySkillAction` also swaps `PhysicsCollider3D.Layer` between `Player` and
`IgnoreProjectile` (same layer-swap idiom `EnemySystem.OnEnemyDied` uses for `DeadEnemy`) — named for
what the layer mechanically does (projectiles ignore it), not for the broader invulnerability concept
it happens to implement; `ProjectileSystem`'s raycast excludes the `IgnoreProjectile` layer entirely,
so a projectile keeps traveling through an invulnerable target instead of being consumed by it.

**Manual step**: the `IgnoreProjectile` Unity/Quantum physics layer needs to exist (Project Settings
→ Tags and Layers) before this compiles/works.

## Roster

| Name | Slot | `SkillData` | Notes |
|---|---|---|---|
| Dash | DashSkill (Shift) | `DashSkillData` | Traversal skill — fixed-distance dash in the current movement-input direction (falls back to `Aim.Angle` facing if no movement input is held), via `KCC.SetActive(false)` + a direct `Transform3D.Position` write each tick (not `KCC.Teleport` — see the class doc comment on why: `Teleport`'s per-tick `HasTeleported` flag suppresses view interpolation and reads as stutter, while a plain write is already safe against KCC desync since `KCC.Update` re-derives its internal position from `Transform3D` every tick regardless of `IsActive`). Wall detection is a `Shape3D` sphere (`BodyRadius`) overlap at the candidate next position with `QueryOptions.HitStatics \| HitKinematics` — not a single fixed-height raycast, which turned out to let the dash pass straight through level-chunk wall geometry undetected; a raycast (same widened query) is still used as a best-effort attempt to back the stop position off the actual hit point by `BodyRadius`, falling back to just not moving that tick if it doesn't connect. |
| — | HeroSkill (E) | *unassigned* | Input button and slot exist; no skill designed yet. |

## Adding a new skill type

Add one new subclass under `Simulation/Assets/Skills/` (`: SkillData`), following `DashSkillData` as
an example:

1. Add whatever type-specific fields it needs (beyond the shared `InitStacks`/`RechargeTime`/`Actions`
   on the `SkillData` base — note `MaxStacks` is deliberately *not* here, see
   [Charges, not a cooldown lock](#charges-not-a-cooldown-lock)).
2. Implement `Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)` — called
   once on the input press-edge. `SkillSystem` has already captured `slot->StartPosition`/
   `TargetPosition` (both = current position) before calling this; override `TargetPosition` here for
   anything that needs a computed destination. Return `true` if the skill fully resolves this same
   tick; return `false` if it needs to keep running over multiple ticks (in which case `SkillSystem`
   switches the slot to `SkillState.Active` and starts calling `Tick` every tick after).
3. If `Begin` can return `false`, also override `Tick(Frame f, ref SkillSystem.Filter filter,
   SkillSlot* slot)` — called every tick until it returns `true`.
4. Override `End(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)` for core per-skill cleanup
   (e.g. `DashSkillData` restoring `KCC.SetActive(true)`) — this is separate from any
   `SkillActionData` in the asset's `Actions` list with `Phase` including `End`, which `SkillSystem`
   invokes right after.
5. Reuse `EnemyMovementUtility`'s type-agnostic helpers where they fit (`IsBlockedByWall` for a
   bool-only check, `GetGroundLayerMask`, `FlatSqrDistance`), but prefer an `OverlapShape` sweep of
   the mover's own `BodyRadius` over a single fixed-height raycast for *detecting* a wall —
   `IsBlockedByWall`'s raycast has to guess a ray height, which turned out to let `DashSkillData`'s
   original wall check pass straight through level-chunk geometry undetected. A supplementary
   `f.Physics3D.Raycast` is still useful once you know you're blocked, to compute a precise stop
   point (`hit.Point` minus `BodyRadius` along the approach direction — see `DashSkillData`'s wall
   stop) — just don't rely on the raycast alone for the yes/no blocked decision. Whichever query you
   use, pass `QueryOptions.HitStatics | QueryOptions.HitKinematics` (not `HitStatics` alone) so
   level-chunk colliders are actually matched — see `IsBlockedByWall`'s own comment for why. Most of
   `EnemyMovementUtility`'s other helpers
   (`MoveKinematicTowards`, `MoveTowardsPoint`, ...) are `EnemySystem.Filter`/`EnemyDataAsset`-
   coupled and don't apply to a KCC-driven player; a player skill drives movement by writing
   `Transform3D.Position` directly (with `KCC.SetActive(false)` for the duration) instead — safe
   against KCC desync since `KCC.Update` re-derives its internal position from `Transform3D` every
   tick regardless of `IsActive`. Avoid `KCC.Teleport` for continuous per-tick movement — it flags
   every call as a hard teleport, which suppresses view interpolation and reads as stutter.
6. Create a `.asset` instance (`Create → Quantum → ...`) under `Resources/Skills/`, assign it to a
   `CharacterSkills` slot on a player prototype, and add a row to the Roster table above.

No `SkillSystem.cs` changes needed for any of this.

## Adding a new composable action

Add one new subclass under `Simulation/Assets/Skills/` (`: SkillActionData`), following
`InvulnerabilitySkillAction`/`ExplodeSkillAction`/`KnockbackOnPathSkillAction` as examples:

1. Set a default `Phase` in the constructor — whatever lifecycle point the behavior most naturally
   belongs at (`ExplodeSkillAction` defaults to `End`, `KnockbackOnPathSkillAction` to `OnGoing`).
   It stays freely reconfigurable per asset instance afterward — changing *when* a shipped action
   fires (e.g. "explode on Begin instead of End") is a one-field edit on the asset, not a new class.
2. Implement `Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
   SkillActionPhase firedPhase)`. If `Phase` combines multiple flags (e.g. `Begin | End`), branch on
   `firedPhase` — it's always called with exactly one bit set, never a combination (see
   `InvulnerabilitySkillAction`).
3. Reach into `slot->TargetPosition`/`StartPosition`/`SpawnedEntity` if the behavior needs to read or
   adjust the skill's own state (e.g. `AvoidColliderSkillAction` clamping `TargetPosition` — this
   runs *after* `SkillData.Begin()` has already set it, so it only ever shortens, never computes a
   direction of its own).
4. Use `DamageUtility.ApplyDamage`/`ApplyKnockback`, `ExplosionUtility.Explode`, or
   `EnemyMovementUtility`'s query helpers (`GetEnemyLayerMask`, `TryFindNearestDecoy`, ...) rather
   than duplicating their logic.
5. Create a `.asset` instance (`Create → Quantum → ...`) under `Resources/Skills/Actions/`, and add it
   to any `SkillData.Actions` list that should have the behavior. The same action asset instance can
   be shared across multiple skills (e.g. one `Invulnerability.asset` reused by every skill that
   wants it).

No `SkillSystem.cs` changes needed for any of this.

## Chained skills (not yet built)

`SkillActionData.Execute` is a one-shot call tied to its *parent* skill's own lifecycle — it has
nowhere to persist state across future ticks. That's fine for a hook that resolves inline (e.g.
`ExplodeSkillAction`), but it means "Dash, and on end also trigger a second skill" doesn't fit today
if that second skill itself needs a multi-tick `Active` phase (e.g. chaining into another Dash):
`CharacterSkills` only has the two player-bound slots, both already spoken for, and overwriting one
would permanently replace the equipped skill rather than chain into it once.

The intended extension, when needed: a small pool of buttonless "triggered" slots on
`CharacterSkills` (e.g. `array<SkillSlot>[2] TriggeredSkills`), only ever started by a new
`TriggerSkillAction`, with `SkillSystem` iterating them the same way it iterates `DashSkill`/`HeroSkill`.
This is additive — a new array field + one new action — not a redesign of `SkillData`/
`SkillActionData`/`SkillSystem`. An *instant*-resolving chained skill (no `Active` phase needed)
doesn't require this at all: a `TriggerSkillAction` (`Phase = End`, say) can already call the
triggered skill's `Begin()` against a scratch, non-persisted `SkillSlot` and resolve it inline if
`Begin()` returns `true`.

## Out of scope so far

- View-side visuals (dash trail, decoy sprite/animation, explosion VFX, ground telegraph) — no
  `SkillData.View.cs`/`SkillVisualStep` equivalent to `EnemyActionData.View.cs`/`AttackVisualStep` exists
  yet. Decoy needs at least a `QuantumEntityView` + placeholder sprite to render at all.

  **Scoped but deliberately deferred** (design call, not yet made) — the mapping is less direct
  than it looks: `EnemyBlobAnimationView.PlayAttackStep` is a ~200-line body-squash dispatcher
  built for the enemy's simple 2-part rig (head/torso); the player's `BlobAnimationView` has no
  equivalent entry point and is a much more elaborate 5-part rig (root/head/torso/legs) already
  fully driven by KCC velocity/state (Idle/Run/Air/Landing) — bolting on a generic step-driven pose
  risks fighting that existing polish. Three options considered, from lightest to heaviest:
  1. **Event-driven kick** — Skill-begin/end Quantum events + a small additive impulse spring on
     `BlobAnimationView`, same shape as its existing `OnPlayerFired` shoot-shake. No generic step
     data, not designer-configurable per skill, but low risk/fast.
  2. **Particles + ground telegraph only** — Reuse `AttackVisualStep`/`TelegraphData` as-is on a
     new `SkillData.View.cs` (`BeginStep`/`OnGoingStep`/`EndStep` + `Telegraph`), add a
     `SkillVisualsView` mirroring `EnemyAttackVisualsView`'s particle-spawn + telegraph logic
     exactly. No `BlobAnimationView` changes — a dash gets a particle burst/trail and e.g. a
     dash-range line, on top of whatever the rig already does organically.
  3. **Full parity with body-squash** — All of (2), plus retrofitting `BlobAnimationView` with an
     analogous `AttackAnimationType` squash/rotation dispatcher, so a skill step can also deform the
     player body. Has to compose with the rig's existing squash instead of just overriding it.
- An XP/level-up system that writes a new `AssetRef` into a slot at runtime — the data shape
  supports it with zero extra plumbing (nothing caches `f.FindAsset(slot->Skill)`), but no
  trigger/UI/currency system exists.
- A skill cooldown/stacks UI widget.
- Chained skills — see above.
