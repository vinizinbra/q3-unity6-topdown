# Elemental Reactions

Fire/Ice/Rock/Lightning each apply their own baseline status on landing (Burn/Slow/Intimidate/
Electrified) - Void is the only element with no baseline, its identity living entirely in
hand-authored `WeaponDataAsset` traits instead. Landing a second element whose pairing status is
already active on a target fires one of 3 elemental reactions immediately - **Thermal Shock**
(Burn+Chill), **Overload** (Burn+Shock), **Shatter** (Chill+Shock). Read this before touching
anything `ElementType`/`StatusEffects`/`ElementalReactionConfig`-related - it's the source of truth
for *why* the numbers and field ownership are shaped the way they are, not just what they are. See
"Current status" at the bottom for what's actually implemented vs. still needs Editor authoring.

## History

### From pairwise Void to stackable Rift Mark (retired further below)

The original version of this system cut Poison and Lightning as standalone elements and added a 4th
element, Void, whose entire purpose was to react with whatever else was already on a target - Void
applied no baseline of its own, was never consumed when it backed a reaction, and the reaction scan
ran between any two of Fire/Ice/Rock/Void's active statuses (6 pairs: Explosion, Freeze, Knockback,
Magma Prison, Stun, Break).

That mechanic was retired in favor of a single stackable status, Rift Mark, that any of 5 elements
(Fire/Ice/Rock/Void/Lightning) could consume to fire exactly one dedicated reaction
(Detonation/Deep Freeze/Rupture/Overload/Singularity) - every reaction became `<element> + RiftMark`,
nothing reacted with anything else.

### From stackable Rift Mark back to pairwise (current model)

Rift Mark is retired in turn, back to a pairwise model - but a different pairwise model than the
original Void-based one above, and built around exactly 3 elements/statuses rather than a scan over
every pair. The stack/mark/lockout machinery, its 5 named reactions, and the whole Weapon
Perk/Rift Mutation content pool built around applying and consuming it are gone entirely (see
`docs/weapon-perks.md`/`docs/rift-mutations.md` for what was cut from those two pools).

In its place: **Lightning gains a real baseline status, Electrified (Shock)**, the same way
Fire/Ice/Rock already had one - reversing its old "no baseline, mark-consumer only" identity. Landing
a second element whose pairing status (Burn/Chill/Electrified) is already active on the target fires
the matching reaction immediately - no consumable mark, no global lockout, no pre-hit snapshot needed:
each element's own baseline application always runs before the reaction check, so whichever element
lands second simply observes the other's status already live, giving order-independence for free. See
"Reaction dispatch and priority" below for the full mechanism.

**Reactions do not consume the statuses that enable them.** Burn/Chill/Electrified are persistent
conditions a reaction capitalizes on, not a resource it spends - each reaction's own cooldown, not
status removal, is what throttles repeat procs. This is a deliberate departure from every earlier
version of this system (the original pairwise scan and Rift Mark both consumed on trigger) - see
"Reactions are non-consuming" below for why.

## Shock (Electrified)

Lightning's baseline status (`StatusEffects.ElectrifiedRemaining`, applied by
`StatusEffectUtility.ApplyElementBaseline`/`ApplyElectrified` the same way Fire→Burn/Ice→Slow are) -
plain overwrite-on-reapply, no tier duration scaling, so Boss stays vulnerable to it the same way it
stays vulnerable to Burn/Slow (soft CC/DoT-flavored, not hard CC).

Gameplay identity: **action disruption**. While Electrified, `StatusEffectSystem.TickElectrified`
ticks `ElectrifiedJoltTimer` down from `ElementalReactionConfig.JoltInterval`; on reaching 0 it applies
a **Jolt** - a brief `StatusEffectUtility.ApplyStagger` (see below) of `JoltStaggerDuration` - fires
`JoltTriggered { Target, Position }` for a one-shot spark, and resets the timer. Purely deterministic
and interval-based, no proc chance anywhere. Shock's whole identity is a *repeatable periodic
interrupt*, not a long stun - Stun remains the separate, stronger hard-CC primitive.

## Stagger: pausing, not stopping

A new CC primitive, distinct from both Stun (full incapacitation - state machine/movement/firing all
freeze) and Root (movement-only lockout): Stagger pauses whatever action-windup timer is currently
counting down, without canceling or resetting the action itself, **and** pins movement for the same
duration (sharing Root's exact freeze - kinematic body + `EnemyMovementUtility.StopMovement`, state
machine otherwise unaffected). A 0.1s Stagger landing during an attack whose windup is 0.5s makes that
attack land at 0.6s instead of 0.5s - delayed, never voided - and the enemy also can't keep sliding
toward the player mid-stumble for that same 0.1s.

- `StatusEffects.StaggerRemaining` - flat unconditional decrement in `StatusEffectSystem`, no
  dedicated Tick method needed (nothing to clean up on expiry, same shape as `RootRemaining`).
- `StatusEffectUtility.ApplyStagger`/`IsStaggered` - modeled on `ApplyRoot`/`IsRooted`, but folds in a
  dedicated `EnemyTierResistanceConfig.TierStatusResistance.StaggerDurationMultiplier` taper instead
  of an immunity window or `ImmuneToHardCC` check. Deliberately **no diminishing-returns immunity
  window** the way Stun has one - Shock's Jolt needs to land repeatably to do its job as a periodic
  interrupt; an immunity window would defeat that identity. Boss is tapered (not immune), consistent
  with "Boss stays vulnerable to soft CC" and the explicit decision not to special-case any one enemy
  archetype.
- Movement hook: `EnemySystem.Update`'s top-level Root gate (kinematic + `StopMovement`, letting the
  state machine keep running unless also Stunned), `UpdateChasing`'s own Root re-check (stops it from
  closing distance specifically), and `EnterRecovering`'s post-attack kinematic restore all now check
  `IsStaggered` alongside `IsRooted` - Stagger reuses Root's exact movement-freeze plumbing rather than
  adding a second one.
- Windup hook: `EnemySystem.UpdatePreparation` skips only the `Enemy.StateTimer -=` line (and the
  phase-transition check immediately after it) for a staggered entity, leaving `StateTimer`/`Phase`
  completely frozen for that tick - without touching the rest of `Update`'s dispatch the way Stun's
  full-method skip does. This is a third, distinct pattern alongside the two that already existed
  there: Stun skips the whole method; Deep Freeze's `AnticipationSlowRemaining` *multiplies* the
  decrement rate (stretches the windup, never stops it); Stagger *skips* the decrement outright for its
  own duration, then resumes counting from exactly where it left off. Scoped deliberately to
  `Preparation`/`Telegraph` only (the one place in the codebase a literal pausable windup timer
  exists) - no equivalent player-side pre-action timer exists to pause (weapons only have a post-shot
  cooldown, skills have no charge-up state), so Stagger has no observable effect on a player.

## Reaction dispatch and priority

`StatusEffectUtility.TryTriggerElementalReaction(f, target, owner, source, newElement, hitDamage)`
is the single dispatcher, called right after an element's own baseline lands (from
`TryApplyElementalStatus`/`TryApplyInfusedElement`, and directly from `BurnEffectData`/`SlowEffectData`
for their own guaranteed element) - never from a status's own periodic tick (`TickBurn`'s DoT damage
and `TickElectrified`'s Jolt both call `DamageUtility`/`ApplyStagger` directly, never back through
this dispatcher), so an already-active status ticking on its own can never masquerade as a fresh
external application and loop back into retriggering a reaction on its own.

Each `case` is an if/else-if over the two other statuses, so a hit can fire **at most one** reaction,
selected by a fixed, deterministic priority - Thermal Shock > Overload > Shatter, consistent across
all three cases:

| New element | Checks (in order) | Reaction |
|---|---|---|
| Fire | Chill active? → | Thermal Shock |
| | else Electrified active? → | Overload |
| Ice | Burn active? → | Thermal Shock |
| | else Electrified active? → | Shatter |
| Lightning | Burn active? → | Overload |
| | else Chill active? → | Shatter |

If the higher-priority reaction is still on its own cooldown, `TryTrigger*` simply returns `false` and
**nothing else happens that hit** - no fallback to the next-priority reaction, no VFX, no damage. The
next fresh elemental hit gets another chance once that cooldown clears. This is what keeps a sustained
elemental build "event-driven, not timer-driven" (see "Reactions are non-consuming" below) - a
reaction never fires just because its cooldown happened to expire; it still needs a new qualifying hit
to land.

## Reactions are non-consuming

None of the 3 reactions remove or shorten the statuses that triggered them. Burn/Chill/Electrified all
keep counting down on their own independent timers exactly as if the reaction never fired. This is a
deliberate design choice for this game's shape (large hordes, an auto-firing weapon landing many
repeated hits): if a reaction consumed its inputs, a build would have to fully rebuild both statuses
from zero after every single proc, which reads as punishing rather than rewarding a sustained
elemental setup. Instead:

- Statuses are **persistent conditions** - once both halves of a pair are up, they just sit there.
- A reaction's own cooldown (`ThermalShockCooldownRemaining`/`OverloadCooldownRemaining`/
  `ShatterCooldownRemaining`) is the **only** throttle - not status removal.
- A sustained Fire+Ice target keeps producing Thermal Shock procs for as long as the player keeps
  landing fresh Fire/Ice hits and each cooldown keeps clearing, without ever needing to "come back
  online" from a cold start.

This composes for free with **Unstable** (unchanged, not touched by this system) - since statuses
survive through however many reactions fire on an enemy, whatever Unstable finds still active at the
moment of death (e.g. Burn+Chill, if the target was never hit with the *other* pairing) transfers
exactly as it always has. No special-case Unstable+Thermal Shock/Overload/Shatter code was added or is
needed - the two systems read the same live status state and never need to know about each other.

## Thermal Shock (Burn + Chill)

**Geometry: POINT.** Single-target burst - deliberately not an AoE (unlike the retired Detonation
reaction), so it reads as a priority-target finisher against Elites/Bosses without also clearing a
crowd around them.

- `TryTriggerThermalShock` (`StatusEffectUtility.cs`): gates/sets `ThermalShockCooldownRemaining`,
  deals `hitDamage * ElementalReactionConfig.ThermalShockDamagePercent` (200% by design - see that
  field's own comment for why Thermal Shock can afford to hit far harder than Overload's own 50%
  initial-hop percent) directly to the target (`DamageUtility.ApplyDamage`, `bypassOutgoingResolution:
  true`, tagged `ElementType.Fire` + `reactionProc: true`), fires
  `ThermalShockTriggered { Target, Position }`. `reactionProc` (→ `EventEntityDamaged.ReactionProc`) is
  what actually lets the view tell this apart from a plain Burn tick - both tag the same `Fire`
  element, so `Element` alone can't (see that field's own comment).
- Presentation: `EffectsManager.thermalShockEffectPrefab` (orange+blue, "a short white-hot flash"),
  falling back to a tinted `defaultAreaBlastEffect` at a fixed reference scale (the event carries no
  radius - this is a point effect, not an area one). Its floating damage number gets its own dedicated
  color too - `DamageNumberKind.ThermalShockDealtByMe`/`TakenByMe` (hot pink/magenta, bumped size like
  a crit but smaller) - `DamageFeedbackManager.ResolveElementalKind` picks these over Burn's own kinds
  whenever `Element == Fire` and `ReactionProc == true`.

## Overload (Burn + Shock)

**Geometry: LINE/CHAIN.** Sequential chain damage, A→B→C→D - never a fan-out from the origin.

- `TryTriggerOverload` deals `hitDamage * OverloadInitialDamagePercent` to the origin immediately (a
  percent of the triggering hit's own damage, same DamagePercent-off-the-triggering-hit convention
  Burn/Rupture already use, not a flat number), fires `OverloadTriggered { Origin, OriginPosition }`,
  then **parks the chain's continuation state** on the origin entity's own `StatusEffects`
  (`OverloadChainOwner`/`Source`/`Position`/`Visited[8]`/`VisitedCount`/`HopsRemaining`/`HopTimer`/
  `CurrentDamage`) instead of resolving every hop synchronously in the same frame.
- `StatusEffectSystem.TickOverloadChain` ticks `OverloadChainHopTimer` down every frame the chain is in
  progress (`HopsRemaining > 0`); on reaching 0 it calls
  `StatusEffectUtility.TryAdvanceOverloadChain`, which finds the nearest not-yet-visited enemy within
  `OverloadChainRadius` of the chain's current logical position (`TryFindNextChainTarget`, adapted from
  `WeaponPerkUtility.TryFindNearestEnemy` with an added visited-exclusion list read off the persisted
  `Visited` buffer, plus an explicit `EntityRef` ordinal tie-break for determinism beyond the overlap
  query's own hit ordering), deals `OverloadChainCurrentDamage * OverloadChainDamagePercent` and stores
  the result back into `OverloadChainCurrentDamage` for the *next* hop to build from - **the chain
  decays hop over hop** rather than every hop dealing the same flat, disconnected number - fires one
  `OverloadChainLink { Target, From, To, Distance }` per hop, and resets the hop timer to
  `OverloadChainDelay`.
- **The chain propagates over real simulated time** (`OverloadChainDelay` seconds between hops, not
  instantly in one frame) - so a travel-particle "jump" between enemies reads in sync with when the
  damage actually lands, rather than needing its own disconnected view-side timing. The chain's state
  lives on the ORIGIN entity even as its logical position moves to other entities each hop - if the
  origin is destroyed mid-chain, `StatusEffectSystem` simply stops iterating it and the chain quietly
  stops (an accepted simplification, not a gap to fix).
- Chain damage is **raw** - a direct `DamageUtility.ApplyDamage` call that bypasses
  `HitEffectUtility`/element application entirely, so a chained hit can never itself apply a status or
  trigger another reaction (no recursive reaction explosions).
- Bounded and loop-safe by construction: capped at 8 visited slots (1 origin + up to 7 hops),
  `OverloadMaxChainTargets` further caps real hop count, and the visited-exclusion list prevents
  re-hitting the same enemy twice.
- Floating damage numbers: `DamageNumberKind.OverloadDealtByMe`/`TakenByMe` (electric yellow-white,
  bumped size) - both the initial hit and every chain hop are tagged `element: ElementType.Lightning,
  reactionProc: true`, and `DamageFeedbackManager.ResolveElementalKind` colors any Lightning-tagged hit
  as Overload (nothing else currently deals Lightning damage, but `reactionProc` is read here too for
  symmetry/future-proofing rather than assuming that stays true forever).
- Presentation: `EffectsManager.overloadOriginParticlePrefab` (flash at the origin),
  `overloadTravelParticlePrefab` (`TravelOverloadSegment` - a genuine traveling instance, started
  looping at `From` and animated toward the target over real seconds read live off
  `ElementalReactionConfig.OverloadChainDelay` itself, not a separately authored view-side duration
  that could drift out of sync with it), optional `overloadImpactParticlePrefab` once it arrives. The
  destination is re-resolved to the target's LIVE position every frame of the travel (not the static
  `To` snapshot) so the spark tracks a moving enemy instead of arriving at wherever they used to be.
  Electric yellow-white fallback tint, kept distinct from Thermal Shock's orange+blue and Shatter's
  icy blue.
- Alternative presentation: `overloadChainLinePrefab` (`BeginOverloadChainLine`/`AppendOverloadChainLink`/
  `RunOverloadChainLine`) is ONE persistent `LineRenderer` per chain (keyed by `Origin`, the same entity
  `OverloadTriggered` fired on) spanning every entity the chain has hit so far - one point per entity in
  hop order, always directly connected, no travel/growth animation: a fresh hop just appends its own
  point immediately. Every point re-resolves its owning entity's LIVE position every frame, so the whole
  chain visually follows if any of its enemies keep moving. Takes priority over
  `overloadTravelParticlePrefab` when assigned; not pooled (the chain is cooldown-gated and capped at 8
  visited slots, nowhere near projectile-hit frequency). `OverloadChainLink` carries `Origin` precisely
  so concurrent chains on different origins never get mixed into the same line. Between each pair of
  consecutive anchors, `overloadChainLineJitterSegments` interior points are inserted, each nudged along
  a perpendicular by a random `overloadChainLineJitter`-sized offset, so the bolt reads jagged rather
  than dead-straight - the offsets themselves only re-randomize every
  `overloadChainLineJitterRefreshInterval` real seconds (not every frame), which is what makes it read
  as an electric crackle instead of continuously wiggling; the anchors still re-track their own live
  entity positions every frame regardless. There's no explicit "chain ended" event - the line is
  considered finished, and starts fading its ALPHA (`startColor`/`endColor`, not width - a beam thinning
  out reads as retracting, alpha dropping reads as dissipating in place) to 0 over
  `overloadChainLineFadeDuration` before being destroyed, once `overloadChainLineIdleTimeout` real
  seconds pass with no new hop (comfortably longer than `OverloadChainDelay`, so a still-hopping chain
  never times out between two hops, and the fade never starts while the chain is still active).

## Shatter (Chill + Shock)

**Geometry: RADIUS.** AoE control reaction - no pull, no knockback, no new displacement mechanic (this
replaces an earlier "Static Collapse" design that *did* pull enemies inward via
`DamageUtility.ApplyKnockback` with the direction inverted, carrying forward the retired Singularity
reaction's mechanism; that pull behavior was cut in favor of pure AoE Stagger before Static Collapse
ever shipped).

- `TryTriggerShatter` gates/sets `ShatterCooldownRemaining`. The entity that triggered it (the
  reaction target) becomes the center and **never itself moves** - it gets a full
  `StatusEffectUtility.ApplyStun` (`ShatterPrimaryStunDuration`), the one enemy that actually landed
  the combo taking the hardest hit. Every other valid enemy caught in `ShatterRadius`
  (`f.Physics3D.OverlapShape`, center excluded) gets `ShatterAreaStaggerDuration` (a SHORT
  `ApplyStagger`, the same primitive Shock's own Jolt uses) - the pack around the primary is
  interrupted, not fully disabled; only the primary itself is hard-disabled.
- `ShatterDamage` is optional, 0 by default - Shatter's identity is control, not damage. When raised
  above 0 it hits every affected enemy (primary + nearby) uniformly, applied the same raw way
  Overload's chain damage is (bypasses `HitEffectUtility`, so it can never itself trigger another
  reaction).
- Elite/Boss get no special-cased behavior - reusing `ApplyStun`/`ApplyStagger` as-is means the
  primary's Stun already respects Boss's `ImmuneToHardCC`/tier duration multipliers/the shared Stun
  diminishing-returns window, and the nearby Stagger already respects `StaggerDurationMultiplier`, both
  the same way every other consumer of those primitives does - no Shatter-specific special-casing. A
  Boss landed as the primary simply won't be stunned (reaction still fires, nearby enemies are still
  staggered), consistent with "don't fail the whole reaction just because the center resists part of
  it."
- Presentation: `EffectsManager.shatterEffectPrefab`, authored at reference radius 1 and scaled by
  `e.Radius` (the real `ShatterRadius`) so the visual reads at the actual gameplay extent. Icy blue
  with yellow lightning accents - a short angular "crack", not an implosion or explosion; no pull/
  vortex visual, no persistent cloud. `ShatterTriggered` only plays this once, at the primary/Center -
  every secondary enemy caught in the AoE also fires its own `JoltTriggered { Target, Position }` (the
  same one-shot spark Shock's own Jolt uses, since the AoE Stagger is the same `ApplyStagger`
  primitive), so each one gets its own hit feedback rather than only the primary reading as affected.

## First-element rest tint

A cosmetic-only marker of which element FIRST ever landed a baseline status on an entity, keyed to
that SPECIFIC status's own live active/inactive state - tinted while it's active, reverted back to the
entity's original rest color the instant it expires. Never switches to track a later different
element, even if one lands afterward - only ever the first.

- `StatusEffects.FirstElementApplied` (Neutral until set) - written exactly once, by a shared
  `StatusEffectUtility.MarkFirstElementApplied(status, element)` helper called at the end of each of
  `ApplyBurn`/`ApplyIce`/`ApplyIntimidate`/`ApplyElectrified` (Rock included, even though it has no view
  color yet - see below), regardless of which caller/path actually triggered it (a normal
  elemental-chance roll, `TryApplyGuaranteedBurn`, a perk-infused hit, ...) - so nothing needs to
  remember to hook this at every individual application site. No event - `StatusEffectUtility.
  GetFirstElementApplied(f, entity)` is a plain view-facing read of the field.
- `HitFeedback.UpdateElementalRestTint`, called every `QUpdate` (same live poll-and-toggle shape as the
  Freeze Mark block right above it in that file), reads `GetFirstElementApplied` and, if it resolves a
  tint (`fireRestTint`/`iceRestTint`/`lightningRestTint` - Rock has none authored, not requested),
  checks whether THAT element's own status is still active right now (`IsBurning`/`IsSlowed`/
  `IsElectrified`/`IsIntimidated`) and only writes `restColor` (the color every hit/heal/shield/etc.
  flash tweens back down to - see `ApplyFlash`) on an active/inactive transition: the tint while active,
  `_originalRestColor` (captured once in `InitializeSprites`, before any tint can touch it - also reset
  on every pooled-enemy respawn) the moment it goes inactive. On that same transition it also actively
  stops any in-flight flash tween and repaints the sprites to the new `restColor` directly (same
  stop-then-set shape as `Die()`/`Respawn()`) - `restColor` is only ever READ by `ApplyFlash` as a
  future tween destination, so without this the sprite would stay stuck at whatever color its last
  flash left it (e.g. a hit landing right before the status expires) with nothing left to ever repaint
  it again. Jolt itself has no `HitFeedback` color flash of its own anymore - removed as redundant once
  this rest tint already shows Electrified is active for its whole duration (only the punch-scale
  flinch, `HitFeedback.joltPunchScaleStrength`/`_enemyBlobAnimationView.PunchScale`, remains for Jolt) -
  but the repaint-on-transition fix above still matters for any OTHER flash (a normal hit, Burn tick,
  ...) that can still land right at the transition.
- Same shared-config shape as every other `HitFeedback` color: `PlayerFxConfig.FireRestTint`/
  `IceRestTint`/`LightningRestTint` overwrite the 3 local fields once in `Awake` when a hero's
  `HitFeedback` has an `fxConfig` assigned (`ApplyFxConfig`), so every hero reads the same values.
  Enemies/objects leave `fxConfig` unassigned and keep their own local Inspector values, same as every
  other flash color here.
- Enemies are the only practical target today (this doc's own "Current status" notes enemies never
  deal elemental damage back), but nothing here is enemy-specific - a player hit by a future enemy
  elemental attack would tint/revert exactly the same way.

## Configuration - `ElementalReactionConfig`

Same convention as `EffectConfig`: a dedicated, `[Header]`-grouped asset, referenced via
`RuntimeConfig.ElementalReactionConfig`. Every field is elemental-reaction-domain-owned - the class
was completely gutted and repurposed when Rift Mark was removed (every one of its old fields belonged
to that system).

| Field | Role |
|---|---|
| `ElectrifiedDuration` | Lightning's baseline duration |
| `JoltInterval` | Seconds between Jolts while Electrified |
| `JoltStaggerDuration` | Stagger duration each Jolt applies |
| `ThermalShockTriggerCooldown`/`ThermalShockDamagePercent` | Burn+Chill reaction |
| `OverloadTriggerCooldown`/`OverloadInitialDamagePercent`/`OverloadChainDamagePercent`/`OverloadChainRadius`/`OverloadMaxChainTargets`/`OverloadChainDelay` | Burn+Shock reaction |
| `ShatterTriggerCooldown`/`ShatterRadius`/`ShatterPrimaryStunDuration`/`ShatterAreaStaggerDuration`/`ShatterDamage` | Chill+Shock reaction |

All data-driven, no hardcoded numbers in code. MVP placeholder defaults (~0.75-1.0s cooldowns) are
decisive starting points, not final balance.

## Field ownership

Same convention as every other system domain here: a reaction/element may only reuse an existing
config field if every other consumer of that field is being removed in the same pass; otherwise it
gets its own dedicated field. Two fields under the old Rift Mark umbrella were **not** deletable
despite living on `ElementalReactionConfig`/`StatusEffects` at the time, because they're generic
primitives with other live callers:

- `StatusEffects.AnticipationSlowRemaining`/`AnticipationSlowMultiplier` +
  `StatusEffectUtility.ApplyAnticipationSlow`/`GetAnticipationMultiplier` - also driven by the
  standalone, freely-authorable `FreezeEffectData` skill effect. Survives untouched; only its old
  Rift-Mark-reaction *trigger* (the retired "Deep Freeze" reaction) went away.
- `StatusEffects.RuptureRemaining`/`RuptureDamageMultiplier` + `StatusEffectUtility.ApplyRupture`/
  `GetIncomingDamageMultiplier` - called directly by Brute's Groundbreaker, the Scrapjaw boss's
  combo-chain finish, Scrapstorm, a wall-hit charge expose, and Lux's Sentry Overload. Survives
  untouched; only its old Rift-Mark-reaction trigger (the retired "Rupture" reaction) went away.

## Multiplayer and determinism

- Every timer (`ElectrifiedRemaining`/`ElectrifiedJoltTimer`, `StaggerRemaining`, each reaction's own
  `*CooldownRemaining`, `OverloadChainHopTimer`) is a plain `FP` countdown ticked by `f.DeltaTime` in
  `StatusEffectSystem`, Quantum's fixed-tick deterministic delta - no `UnityEngine.Time` anywhere in
  this system.
- Overload's chain target selection (`TryFindNextChainTarget`) and Shatter's radius query both use
  `f.Physics3D.OverlapShape`, never Unity Physics, with an explicit `EntityRef` ordinal tie-break on
  top of the overlap query's own ordering for the chain search specifically, so a same-distance tie
  between two candidates resolves identically on every client.
- Any player's hit can apply an element or trigger a reaction - none of this branches on which
  client is local; ownership (`EntityRef owner`) is tracked only for damage/event attribution.
- Simultaneous applications across players resolve in whatever order Quantum's deterministic
  system/entity iteration processes them that tick - same guarantee every other `StatusEffects` field
  in this codebase already relies on.

## UI and VFX

- `CharacterUiWidget`: `electrifiedIndicator`/`staggerIndicator` (new), following the existing
  `StatusIndicator` pattern (`deepFreezeIndicator`/`ruptureIndicator` unchanged - those statuses
  survive). `riftMarkIndicator` removed.
- `StatusEffectsManager`: `electrifiedParticlePrefab`/`staggerParticlePrefab` trackers (new), same
  `Update`/`EndFrame` shape as every other status. Stagger's own brief duration (`JoltStaggerDuration`)
  means this tracker firing on every Jolt already gives a natural "pulse per Jolt" presentation with no
  extra event needed. `riftMarkParticlePrefab` removed; `freezeParticlePrefab`/`ruptureParticlePrefab`
  unchanged.
- `HitFeedback`: `riftMarkMaterial`/its application flash removed; `freezeMaterial` unchanged.
- `EffectsManager`: `joltEffectPrefab`, `thermalShockEffectPrefab`, `overloadOriginParticlePrefab`/
  `overloadTravelParticlePrefab`/`overloadImpactParticlePrefab`, `shatterEffectPrefab` (new, all
  currently fall back to a tinted `defaultAreaBlastEffect` until bespoke prefabs are authored, same
  "code's ready, needs Editor authoring" gap every other one-shot reaction VFX in this file already
  carries). `detonationEffectPrefab`/`riftMarkedExplodeEffectPrefab`/`overflowingRiftPulsePrefab`
  removed; the old `singularityEffectPrefab` slot's mechanism (radius pull) was cut before shipping,
  not carried forward - Shatter's own prefab is a fresh slot, not a rename.
- `DamageFeedbackManager`: `ThermalShockDealtByMe`/`TakenByMe`, `OverloadDealtByMe`/`TakenByMe` (new,
  see each reaction's own section above for exact colors) - `ResolveElementalKind` reads
  `EventEntityDamaged.ReactionProc` (new) alongside `Element`, since Element alone can't tell Thermal
  Shock's own proc apart from a plain Burn tick (both tag `Fire`).
- **Color rule**: Thermal Shock orange+blue, Overload electric yellow-white, Shatter icy blue with
  yellow lightning accents - deliberately distinct from each other and from the old Rift Mark
  hot-pink `#FD3971`/Void purple palette, which is fully retired.

## Current status

Implemented and live: `ElementType.Lightning` now applies `Electrified` as a real baseline (joining
Fire/Ice/Rock); `StatusEffects.qtn`'s `ElectrifiedRemaining`/`ElectrifiedJoltTimer`/`StaggerRemaining`/
`ThermalShockCooldownRemaining`/`OverloadCooldownRemaining`/`ShatterCooldownRemaining`/`OverloadChain*`
fields; `ElementalReactionConfig`'s full field replacement; `StatusEffectUtility`'s
`TryTriggerElementalReaction` dispatcher and the 3 `TryTrigger*` reactions (non-consuming, cooldown-
gated, deterministic priority); `ApplyElectrified`/`IsElectrified`/`ApplyStagger`/`IsStaggered`;
`EnemySystem.UpdatePreparation`'s Stagger hook; `EnemyTierResistanceConfig.StaggerDurationMultiplier`
per tier; every Rift Mark field/method/event/UI-VFX-slot/Weapon-Perk/Rift-Mutation-pool-entry this
replaced is deleted, not deprecated - see `docs/weapon-perks.md`/`docs/rift-mutations.md` for exactly
what was cut from those two content pools. Also implemented: `EventEntityDamaged.ReactionProc` (lets
the view tell a reaction's own proc damage apart from a periodic status tick even when both share the
same `Element`) and the first-element rest tint (`StatusEffects.FirstElementApplied` /
`StatusEffectUtility.GetFirstElementApplied` / `HitFeedback.UpdateElementalRestTint` - see that section
above).

**Not done yet / known simplifications:**
- **No bespoke reaction VFX prefabs authored yet** (`thermalShockEffectPrefab`/
  `overloadOriginParticlePrefab`/`overloadTravelParticlePrefab`/`shatterEffectPrefab` all fall back to
  the tinted default blast) - "code's ready, needs Editor authoring", same gap this project's other
  systems already carry.
- **Enemies never deal elemental damage** - every `EnemyDeliveryData` hardcodes `ElementType.Neutral`,
  a pre-existing limitation this rework didn't touch, so Electrified/reactions are currently a
  player-applied-to-enemy-only mechanic.
- **No automated coverage** - no Quantum simulation test harness exists anywhere in this project (the
  two EditMode test files that covered Rift Mark's pure stack-math/threshold helpers were deleted
  alongside it, since neither pure helper survived). Verify manually in-Editor: apply Burn then Chill
  to confirm Thermal Shock fires exactly once and both statuses remain active afterward; reverse the
  order to confirm it's genuinely order-independent; re-apply within the cooldown window to confirm no
  second trigger; cluster several enemies and trigger Overload to confirm the chain hops sequentially
  (not a fan-out) with a visible delay between hops; trigger Shatter near a mixed group including a
  Boss to confirm the primary gets a strong Stagger, nearby enemies get a short one, and the Boss is
  tapered rather than immune; stand an enemy mid-windup and land a Jolt to confirm its attack lands
  later rather than being canceled.
