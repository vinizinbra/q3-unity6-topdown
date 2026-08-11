# Experience Drops

A dying `Enemy` drops a physical `ExpOrb` pickup, which any nearby player walks over to collect,
adding to **one shared run-wide total and level** - this is co-op, so exp is pooled across every
current player rather than tracked per-character (whichever player actually reaches an orb just
determines the collection radius via their own `PickupRangeMultiplier`; the exp itself always
credits the shared run total). There was no XP/loot/pickup system of any kind before this - this is
the first one, and it deliberately stops at "track a total and a level": nothing today
auto-triggers the existing (debug-only) perk/skill-upgrade grant commands on a level-up. That's a
separate, later piece of work.

## Runtime flow

```
DamageUtility.ApplyDamage - the moment health hits zero, before branching on enemy tier
  -> f.Events.EntityDied(target, owner)
  -> ExperienceUtility.TrySpawnDrop(f, target, owner)
       owner == EntityRef.None (no traceable instigator - fall/void death, an un-authored level
       hazard)                                  -> drop nothing
       target has no Enemy component             -> drop nothing (player/sentry deaths never drop)
       EnemyTierStatsConfig.Get(data.Tier).ExpValue <= 0 -> drop nothing
       RuntimeConfig.Prefabs.ExpOrbPrototype not assigned -> drop nothing (logged)
       otherwise: f.Create the orb prototype at the dying enemy's position, stamp ExpOrb.Value,
       add DestroyAfterTime seeded from ExperienceConfig.OrbLifetime

ExpOrbSystem (every tick, every ExpOrb entity)
  -> broadphase player query around the orb (EnemyMovementUtility.FindPlayersInRadius)
  -> for the first player within ExperienceConfig.PickupRadius * that player's own
     CharacterStats.PickupRangeMultiplier:
       ExperienceUtility.Grant(f, orb.Value) -> f.Destroy(orb)

ExperienceUtility.Grant (credits the shared run total, not any one player)
  -> Frame.Global.TotalExperience += amount
  -> xpRequirementMultiplier = ResolveXpRequirementMultiplier(f)   // see docs/run-curves-coop-scaling.md
  -> while Level < ExperienceConfig.MaxLevel && TotalExperience >= RequiredExperience.Evaluate(Level + 1) * xpRequirementMultiplier:
       Level++

ExpOrbSystem also fires f.Events.ExpOrbCollected(collector, position, amount) right alongside
Grant - purely a View hook (see "View / presentation" below), no simulation consumer. Collector
is only used to scope the pickup glow to whichever character touched the orb - it does NOT gate
the shared bar/flying effect, which every client plays identically regardless of collector.
```

## Why `owner == EntityRef.None` is the environment/hazard signal

There is no dedicated "player vs. environment" flag anywhere in the damage pipeline -
`DamageSource` (`Weapon`/`Skill`/`None`) picks which multiplier applies, it does not mean
"environment." The only two environment-damage paths that exist today (`EnemySystem.
CheckFallDeath`'s fall/void death, and `AreaDamageSystem.ResolveOwner`'s un-authored/hand-placed
`AreaDamage` hazard) both already pass `owner = EntityRef.None`, so that's the signal this reuses
rather than inventing a new one. A player-owned hazard (e.g. a skill's fire trail, which carries a
real `AreaOwner.Owner`) still has a traceable instigator and correctly still drops - the rule is
"was there an instigator," not "was this any kind of area/DoT."

Director "retirement" (`EnemyLifecycleSystem.Retire`, stale/off-screen enemy cleanup) destroys
through a completely separate `f.Destroy` path that never calls `ApplyDamage`/fires `EntityDied` -
so it's excluded automatically, no extra guard needed.

## Files

**Simulation (`Assets/_QuantumUser/Simulation/QTN/`)**
- `ExpOrb.qtn` - `component ExpOrb { FP Value; }`.
- `Experience.qtn` - **new** `global { FP TotalExperience; Int32 Level; }`, merged into the same
  `Frame.Global` struct as `Chunk.qtn`/`SurvivalDirector.qtn`'s own global blocks. One shared total
  for the whole co-op run, same reasoning as `DirectorBudget` - not a `CharacterStats` field, since
  this isn't per-player. Both start at their zero default (no explicit seed, same as Director's own
  globals) - `Level` counts level-ups earned so far, NOT the displayed level: `RequiredExperience`
  is authored 1-indexed (its first keyframe is "level 1 costs 0 exp"), so every consumer
  (`ExperienceUtility.Grant`, `ExpBarUiWidget`) evaluates/displays `Level + 1`, never `Level`
  directly. Evaluating the curve at `Level`/`Level + 1` instead (the original version of this
  code) clamps both to the same first keyframe while `Level == 0`, producing a permanent `span ==
  0` - this was an actual bug caught once a real curve was authored, not just a cosmetic "starts
  at 0 vs 1" choice.

**Data (`Assets/_QuantumUser/Simulation/Assets/`)**
- `ExperienceConfig.cs` - `FPAnimationCurve RequiredExperience` (X = displayed Level, 1-indexed, Y = cumulative
  TotalExperience required), `MaxLevel`, `PickupRadius`, `OrbLifetime`.
- `Enemy/EnemyTierStatsConfig.cs` - per-`EnemyTier` `ExpValue` (alongside `MaxHealth`/`Cost`/
  `ScaleMultiplier`), resolved via `EnemyTierStatsConfig.Resolve(f, data.Tier)`. Not a field on
  `EnemyDataAsset` itself - see that class's own doc for the tier-consolidation this was folded
  into.

**Systems (`Assets/_QuantumUser/Simulation/Systems/`)**
- `ExperienceUtility.cs` - `TrySpawnDrop` (spawn-on-death) and `Grant` (add exp + recompute level).
  Static utility, mirrors `DamageUtility`'s shape.
- `ExpOrbSystem.cs` - collection. Runs independently, ordered before `DestroyAfterTimeSystem` (it
  also calls `f.Destroy`), same reasoning as `EnemyLifecycleSystem`'s own ordering comment.
- `DamageUtility.cs` - one added line, `ExperienceUtility.TrySpawnDrop(f, target, owner)`, right
  after `f.Events.EntityDied` fires.

**Edited existing files:**
- `Default/RuntimeConfig.User.cs` - `AssetRef<ExperienceConfig>`, `AssetRef<EntityPrototype>
  ExpOrbPrototype`.
- `Default/SystemSetup.User.cs` - registered `ExpOrbSystem`.
- `QTN/Events.qtn` - **new** `event ExpOrbCollected { EntityRef Collector; FPVector3 Position; FP
  Amount; }`.

## View / presentation

Purely cosmetic, client-side reaction to already-synced simulation state - nothing here is
simulation state itself. Two independent reactions to the same `ExpOrbCollected` event, at two
different scopes:

- **Shared, HUD-scoped** (`Assets/_Project/Scripts/UI/Hud/`) - the flying icon + bar flash. No
  local-player filtering: exp is shared co-op state, so every client's bar advances together and
  every client plays this for every orb, regardless of who physically walked over it. `Collector`
  is never read here.
- **Per-character, world-scoped** (`Assets/_QuantumUser/View/Util/HitFeedback.cs`) - a brief glow
  on whichever character's own view actually touched the orb, gated by `e.Collector ==
  _entityRef` - "it was me who grabbed it" feedback, visible to everyone (same visibility model as
  the existing hit/heal flash this was added to), independent of the shared exp credit itself.

- `ExpBarUiWidget.cs` - single scene instance (sibling of `DirectorTimelineWidget` under
  `InGameUi`), `QuantumGlobalMonoBehaviour`. Polls `Frame.Global.TotalExperience`/`Level` and
  `RuntimeConfig.ExperienceConfig`'s curve each `QUpdate` - the `"Lv. N"` text and a `"current/next"`
  progress text (e.g. `"10/100"`, both relative to the current level's own span, `Mathf.CeilToInt`,
  same convention as `CharacterUiWidget`'s health/shield text) update live every frame, but the fill
  `Slider` deliberately does NOT: it holds its last displayed value (only ever snapped directly on
  the very first `QUpdate`, to avoid easing in from Unity's default `Slider` value) until `Flash()`
  runs. `Flash()` (public, called by `FlyingXpManager` once a flying pickup widget lands) lerps the
  fill `Image`'s color (copied from `CharacterUiWidget.ShineShieldFill`/`ShieldShineRoutine`/
  `LerpColor`) AND eases the slider fill toward whatever the real value currently is, together, over
  `sliderLerpDuration`/`flashDuration` - so the bar visibly "catches up" right as the icon arrives,
  not the instant the orb is actually collected (which happens in simulation before the icon has
  even finished flying). If another orb lands mid-lerp, `Flash()` restarts both routines from
  wherever they currently are, bending toward the newer target rather than overshooting a stale one.
  Exposes `LandingPoint` (a `RectTransform`) as the flying widget's destination.
- `FlyingXpWidget.cs` - pooled, mirrors `DamageNumberUiWidget`. Unlike that widget, it projects its
  world start position only **once** at `Play` time rather than re-projecting every `LateUpdate` -
  the orb is already destroyed by the time the pickup event fires, so there's nothing left to keep
  following. Tweens (PrimeTween `Tween.Custom`, same call shape `DamageNumberUiWidget`/
  `MovementRingView` already use) straight from that start point to `ExpBarUiWidget.LandingPoint`,
  then calls `Flash()` on completion.
- `FlyingXpManager.cs` - `QuantumGlobalMonoBehaviour` singleton, mirrors `DamageFeedbackManager`'s
  `QStart`-resolves-camera/canvas + `ObjectPool` + `QuantumEvent.Subscribe` shape. Subscribes to
  `EventExpOrbCollected`, spawns a pooled `FlyingXpWidget` per event.
- `UIHelper.cs` - **new** `TryRectTransformToAnchoredPosition(target, canvas, source, out
  anchoredPosition)`, alongside the existing `TryWorldToAnchoredPosition`. Needed because
  `FlyingXpWidget` and `ExpBarUiWidget.LandingPoint` sit under different parents - `anchoredPosition`
  is only meaningful relative to your own parent, so the landing point has to be re-projected
  through `RectTransformUtility.WorldToScreenPoint` into the flying widget's own parent space
  rather than read off directly.
- `HitFeedback.cs` - **edited**, not new. Added an `EventExpOrbCollected` subscription (`OnExpOrbCollected`, gated on `e.Collector == _entityRef`) and a `pickupGlowColor`/`pickupGlowDuration` pair, reusing the existing `Flash(Color)` tween machinery already used for hit/heal - `Flash` now takes an optional duration so this glow can run longer than the snappy default hit-flash.

## Current status / known simplifications

Code compiles and `ExpOrbSystem` is registered, but **nothing drops or can be collected at runtime
yet** - the following need Editor authoring, none of it done yet:

1. **`ExperienceConfig.asset`** - created at `Assets/_QuantumUser/Resources/ExperienceConfig.asset`
   with a real `RequiredExperience` curve already drawn (1-indexed - level 1 costs 0 exp), but
   **not yet assigned to `RuntimeConfig`** - `QuantumMenuConfig.asset`'s `RuntimeConfig` block has
   `EnemyTierStatsConfig`/`ExplodeOnDeathConfig`/etc. wired but no `ExperienceConfig` entry at all
   yet. Until it's dragged into that slot, `ExpBarUiWidget.QUpdate` bails out on its very first
   guard and shows whatever placeholder text is sitting in the Inspector, not live data.
2. **`ExpOrb` prefab** - no `EntityPrototype` exists carrying the `ExpOrb` component. Needs a new
   prefab (same shape as `Assets/_QuantumUser/Resources/Skills/Lux/Sentry.prefab`) with a visual.
3. `ExpOrbPrototype` also needs assigning on `RuntimeConfig` once the prefab above exists.
4. `ExpValue` is tuned per `EnemyTier` in `EnemyTierStatsConfig`, not per `EnemyDataAsset` -
   already assigned to `RuntimeConfig`/`QuantumMenuConfig` with placeholder values, see that
   asset's own doc for the tier-consolidation this was folded into.
5. **HUD scene/prefab work** - no `ExpBarUiWidget`/`FlyingXpManager` GameObjects exist in
   `QuantumGameScene` yet, and no `FlyingXpWidget` icon prefab exists. Needs: an exp bar (Slider +
   fill `Image` + `TMP_Text`) placed under `InGameUi` next to `DirectorTimelineWidget`, a
   `FlyingXpManager` GameObject wired to a small pooled icon prefab, and `ExpBarUiWidget.LandingPoint`
   pointed at the bar's fill rect. Until this exists, `FlyingXpManager` logs a warning and drops the
   pickup effect on the floor (`ExpBarUiWidget.Instance == null`).

Beyond the missing assets:
- **No magnetism/homing** - an orb sits exactly where it dropped; a player has to walk within
  `PickupRadius * PickupRangeMultiplier` themselves. `ExpOrbSystem`'s broadphase query radius is a
  fixed multiple (`8x`) of `ExperienceConfig.PickupRadius` as a stand-in for "large enough to catch
  any realistic `PickupRangeMultiplier` stack" - if a build ever pushes that multiplier past ~8x,
  widen `ExpOrbSystem.QueryRadiusScale`.
- **No level-up trigger** - `Frame.Global.Level` updates, but nothing reads it to grant a perk/
  skill-upgrade choice to anyone. `GrantWeaponPerkCommand`/`GrantSkillUpgradeCommand` remain
  debug-only (`WeaponPerkDebugTrigger`/`SkillUpgradeDebugTrigger`), same as before this feature.
- **One orb per kill** - `ExpValue` is not split across multiple smaller orbs even for a
  high-value tier.
