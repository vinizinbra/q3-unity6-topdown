# Hold-to-Revive (Alive → Downed → KO)

A player life-state machine (`Alive → Downed → KO`) plus a hold-to-revive channel, built as the
smallest reusable extension of the existing Context Interaction / Base-Skill-button redirect
(`ContextInteraction.qtn`, see `docs/breathing-poi.md`) - the same generic mechanism Cursed
Rift/Healing Shrine/Store/Blacksmith/Traversal Challenge already use. Neither the life-state
machine nor self-revive existed in any form before this pass - a lethal hit on a player used to go
straight to an instant full-heal-and-teleport-to-spawn (the old `DamageUtility.RespawnPlayer`,
deleted entirely by this change). Read `docs/breathing-poi.md` first for the base redirect
mechanism this extends.

## Life state (`PlayerLifeState.qtn`)

```
enum PlayerLifeStateKind : Byte { Alive, Downed, KO }

component PlayerLifeState
{
    PlayerLifeStateKind State;
    FP        BleedOutRemaining; // ticks only while Downed && ReviveHolder == None
    FP        ReviveProgress;    // seconds accumulated toward whichever reviver holds ReviveHolder
    EntityRef ReviveHolder;      // who is currently channeling THIS entity's own revive
}
```

Present on every player entity from spawn (added by hand to each hero `EntityPrototype`), all-zero
default = `Alive`. `DamageUtility.ApplyDamage`'s own lethal-damage branch for a `PlayerLink` entity
now calls `PlayerLifeStateUtility.EnterDowned` directly instead of the old `RespawnPlayer` - the
entity is never destroyed or repositioned, it just stops at 0 Health exactly where it fell.

**Downed is damage-immune** - confirmed with the user, a bleed-out timer (`DownedBleedOutDuration`,
default 20s) is the *only* way a Downed player becomes KO, never another hit. `EnterDowned` adds the
existing `Invulnerable` tag (the same one `CheatDeathUtility`/Burrow enemies already use -
`DamageUtility.ApplyDamage`'s very first gate ignores every hit against it) and leaves it in place
straight through the KO transition; `PlayerLifeStateSystem` is the only thing that ticks
`BleedOutRemaining` down, and only while `ReviveHolder == EntityRef.None` - the timer **pauses the
instant someone starts holding to revive this player**, a deliberate judgment call so a
near-complete Downed revive can never get yanked into KO by an unlucky timer expiry mid-channel.

`EnterDowned` also adds an `Interactable { Kind = Revive, Radius = ReviveConfig.
ReviveInteractionRange, Priority = 0 }` to the Downed player's own entity - this is the entire
integration point with the existing generic proximity scan (`ContextInteractionSystem`, see below);
nothing about that scan's core loop needed to change to let a *player* become a valid interaction
candidate instead of a static POI.

**KO has no revive path at all anymore - confirmed with the user, a deliberate dead end.** Neither a
teammate's hold nor the player's own `SelfReviveCharges` can bring a KO'd player back; the *only* way
back is `Global.BreathingAreaSecured` auto-reviving everyone still incapacitated once the team
actually clears the area (see "Auto-revive on Breathing area secured" below). This was NOT the
original design - KO originally had its own longer (5s) teammate-hold duration and could be
self-revived exactly like Downed, sharing the same `ReviveChannel`/`ReviveTargetKind` mechanism - but
testing showed a 5s uninterrupted hold was rarely achievable under sustained enemy fire (see the
"Revive channel" section's own bug writeup below for the full diagnosis), and the user explicitly
decided to cut KO revival entirely rather than keep tuning it. `EnterKO` now **removes** `Interactable`
(previously left untouched) - a KO'd player is no longer ever a valid `ContextInteractionSystem`
scan candidate, so a teammate simply can't target them for anything. `ReviveTargetKind`/
`ReviveChannel.Kind` were deleted outright along with this (a `ReviveChannel` only ever targets a
Downed player now, so there was nothing left to discriminate by Kind) - see the "Revive channel"
section below for what that simplified to.

`EnterDowned` also calls a new `ProjectileSystem.DestroyOwnedBy(f, target)` - a real bug found and
fixed while testing: `ProjectileSystem` was, and always had been, 100% self-contained (a projectile
only ever destroys itself on its own hit/expire, never checking whether its `Owner` still exists or
what state it's in), and a player entity is never destroyed by going Downed/KO (unlike an enemy,
which really is `f.Destroy`'d on death), so nothing ever cleaned up whatever shots a player already
had in flight the instant before a lethal hit. This was true even under the old instant-respawn
`RespawnPlayer` flow (confirmed via `git show` on its pre-deletion body - it only ever touched
Health/Shield/position/status effects, never `Projectile`) - it just went unnoticed since the player
and camera both snapped away to the spawn point immediately, and stray shots usually expired within
a second or two on their own. It's far more visible now that Downed keeps the player in place for
up to `DownedBleedOutDuration` (~20s default), watching their own last shot still flying. Collects
matching `Projectile` entities into a temp list first, then destroys each via `ProjectileSystem`'s
existing single `Destroy()` termination point (not a raw `f.Destroy`) so `ClearSourceSlot` still
fires too - otherwise a projectile-based skill's own `ProjectilePending` slot could get stuck `true`
forever. Deliberately player-only (matches what was actually asked) - an enemy's own in-flight shots
are just as orphaned today, since nothing subscribes to `ISignalOnEntityKilled` for this.

`PlayerLifeStateUtility.Revive(f, target, reviver)` (shared by a teammate revive and self-revive,
`reviver == target` for the latter) sets `Health.CurrentHealth = MaxHealth *
ReviveConfig.ReviveHealthPercent` **directly** rather than through `HealUtility.ApplyHeal` -
`HealUtility.ApplyFlatHeal` opens with `if (CurrentHealth <= 0) return 0`, a guaranteed silent
no-op against a Downed/KO player's exactly-0 Health. Shield is left untouched, same as
`HealingShrineUtility`'s own heal. `Invulnerable` is left exactly as-is (zero add/remove gap) and
re-justified by a fresh `StatusEffects.ReviveImmunityRemaining` timer (`ReviveInvulnerabilityDuration`,
default 2s) - a second, independent reason the tag can be present, ticked down by
`StatusEffectSystem.TickReviveImmunity`, the exact same guarded-decrement-then-remove shape
`TickCheatDeathImmunity` already established.

`PlayerLifeStateUtility.IsIncapacitated(f, entity)` (`State != Alive`, false/no-op for anything
without the component) is the single incapacitation gate threaded through `ContextInteractionSystem`,
`SkillSystem`, `WeaponSystem` and `PlayerMovementProcessor` - a Downed/KO player cannot move, shoot,
dash, cast their Hero Skill, or start a new POI interaction, full stop (no partial movement for the
incapacitated player themselves - that's reserved for an *Alive* reviver, see below). `ShieldSystem`
also gates on it (confirmed with the user) - a Downed/KO player's Shield stays frozen exactly where
it was rather than quietly recharging while they can't be hit anyway; `HealthRegenSystem` needed no
equivalent change - it already no-ops for them for free, since `HealUtility.ApplyFlatHeal`'s own
`CurrentHealth <= 0` guard (see the `HealUtility` correction above) already blocks it.

## Untargetable to enemies

Confirmed with the user: a Downed/KO player must be fully untargetable, not merely damage-immune -
enemies should stop pursuing/attacking them and go after a still-Alive player instead (or idle if
none are left). This is the mirror image of the existing Burrow feature (`docs/enemy-burrow.md`),
which made an *enemy* untargetable-by-players via the same `Invulnerable` tag - but the two features
touch a disjoint set of files, since "player aims at enemy" (`AimSystem`, the player-cast
`VortexSystem`) and "enemy aims at player" are entirely separate code paths. **Deliberately does NOT
reuse a plain `f.Has<Invulnerable>()` check** the way Burrow's own patch did - `Invulnerable` on a
*player* entity is now three-ways overloaded (Downed/KO, Max's Cheat Death, the post-revive grace
window above), and the latter two must stay targetable. Every patch below uses
`PlayerLifeStateUtility.IsIncapacitated` specifically instead.

- `EnemyMovementUtility.TryFindNearestPlayer` (the root query nearly every player-targeting path
  routes through) now loops its own hit results and skips an incapacitated candidate, mirroring
  `TryFindNearestEnemy`'s existing dying/Invulnerable-enemy skip just below it in the same file.
- `EnemyTargetingData` subclasses that build their own candidate pool from
  `FindPlayersInRadius` (`LargestPlayerClusterTargetingData`, `MostIsolatedPlayerTargetingData`,
  `RandomPlayerTargetingData`) each independently skip incapacitated candidates too, since they
  don't route through `TryFindNearestPlayer`. `CurrentTargetLockTargetingData` additionally drops
  its own held lock the instant it goes incapacitated, rather than re-affirming it forever.
- **The real fix, not just the acquisition-time ones above:** `Enemy.Target` is otherwise fully
  sticky once an enemy is Chasing (re-resolved only on the rare Idle→Chasing edge, target-destroyed,
  or leash-lost - see `EnemySystem`'s own header comments) - an enemy that locked onto a player
  *before* they went Downed would otherwise keep "chasing" them forever (harmlessly whiffing on
  `Invulnerable`) instead of dropping back to Idle and re-acquiring someone else. `EnemySystem.
  UpdateChasing`/`UpdateRecovery` now treat an incapacitated target exactly like a destroyed one -
  same existing early-exit-to-Idle branch, one extra condition. `BossSystem`'s own periodic
  `TickRetarget` needed no separate patch - Boss entities flow through this same `EnemySystem`
  machinery underneath, so the fix already covers them.
- `ChargeDeliveryData.CanBegin`'s own player-raycast (validates whether a charge is worth starting,
  independent of `TryFindNearestPlayer`) and `EnemyDecisionUtility.TargetCountScore` (an AoE action's
  "worth it" heuristic) both also exclude incapacitated players, for consistency - neither is a hard
  targeting decision, but both could otherwise be swayed by a player who can't actually be hit.

## Revive channel (`Poi/Revive.qtn`) - TEAMMATE revive of a DOWNED player only

```
component ReviveChannel   // per-REVIVER
{
    EntityRef Target;
}
```

Component presence == actively channeling, same convention `CursedRiftInteraction`/`LevelUpChoice`
already use. Always a teammate reviving a Downed player - self-revive is a deliberately separate,
unrelated instant path (`SelfReviveCommand`, see below) that never creates a `ReviveChannel` at all
(reworked mid-implementation from an earlier design where `Self` was a third `ReviveTargetKind`
sharing this same component/system, at the user's explicit direction once the self-revive UI moved
into its own dedicated widget). `ReviveTargetKind`/`ReviveChannel.Kind` themselves were removed
entirely once KO revival was cut (see "Life state" above) - a channel can now only ever target a
Downed player, so there was nothing left to discriminate.

**Deliberately does not reuse `ContextInteraction.ActiveTarget`.** `ContextInteractionSystem` fully
re-resolves its own closest-in-radius scan from scratch every tick, with zero stickiness, even while
`Busy` - confirmed by reading its source, not assumed. If the revive UI read `ActiveTarget` while
channeling, a reviver who happened to drift closer to some other POI mid-hold would silently blank
the revive prompt on their real (locked) target. `ReviveChannel.Target` (reviver-side) and
`PlayerLifeState.ReviveHolder`/`ReviveProgress` (target-side) are their own independent lock,
untouched by whatever `ContextInteractionSystem` resolves for either player on any given tick.

`ReviveChannelSystem` (`SystemMainThreadFilter<ReviveChannel, PlayerLink, Transform3D>`, filters on
the **reviver**) ticks every active channel - always a TEAMMATE revive of a Downed player
(self-revive is the entirely separate `SelfReviveCommand` path below, never creates a
`ReviveChannel` at all):
1. Target gone, or no longer `Downed` (already `Alive`, or - shouldn't be reachable, see below -
   `KO`) → `ReviveUtility.Cancel`.
2. Reviver themselves incapacitated, or out of `ReviveInteractionRange` → `Cancel`.
3. `Input.HeroSkill.IsDown == false` (read directly off this tick's raw input, not through
   `SkillSystem`'s own local-variable neutralization, which never mutates the underlying `Input`
   struct) → `Cancel`.
4. Else → `target.ReviveProgress += DeltaTime`; at `ReviveConfig.DownedReviveDuration` →
   `PlayerLifeStateUtility.Revive`, remove the channel.

(The target reaching `KO` mid-channel shouldn't actually be reachable at all - the bleed-out timer
that drives Downed → KO is itself paused the instant `ReviveHolder != None`, see "Life state" above
- but step 1 checks `State != Downed` rather than just `!= Alive` anyway, the same "never trust it,
re-validate everything" discipline every other check here already follows.)

**Real bug found and fixed after initial testing: reviving mid-combat was effectively impossible,
especially for a 5s KO revive.** The original MVP design had every cancel trigger (release, leave
range, reviver incapacitated) fully reset `ReviveProgress` to 0, and a *separate* damage-pause
mechanism (`DamagePauseRemaining`, a 0.5s freeze-don't-reset on taking a hit) for the "took damage"
case specifically. In practice the reviver is almost always the only non-incapacitated player left
standing, so they draw continuous enemy attention; between constant re-pausing (progress never
advancing while under fire) and a 3-unit `ReviveInteractionRange` that a full `Cancel` on the
slightest kite/dodge would zero out entirely, an uninterrupted 5s window during active combat was
rarely reachable at all - the first attempt that actually succeeded tended to be the very next hold
once combat ended entering a Breathing Break, which read as the target "automatically" coming back
rather than a revive anyone had actually completed.

Reworked (confirmed with the user) so **no cancel trigger resets progress anymore - it decays
instead.** `Cancel` (`ReviveUtility.Cancel`, called for every trigger above, including a fresh hit -
see below) now only clears `ReviveHolder`/removes the `ReviveChannel`; `ReviveProgress` itself is
left exactly where it was. `PlayerLifeStateSystem` - which already iterates every player carrying
`PlayerLifeState` each tick for the bleed-out timer - now also decays a Downed/KO player's own
`ReviveProgress` back toward 0 at `ReviveConfig.ReviveProgressDecayRate` (default 0.5, half the rate
progress builds at) whenever `ReviveHolder == EntityRef.None`, i.e. whenever nobody is actively
holding. A teammate resuming the hold later picks up roughly where an interrupted attempt left off,
provided they get back to it before it fully decays away, instead of always restarting from zero.

**Damage now interrupts (cancels) outright, not pauses.** `ReviveDamageInterruptSystem` (renamed
from `ReviveDamagePauseSystem`) reacts to `Combat.qtn`'s `OnHealthDamageApplied`/
`OnShieldDamageApplied` - the exact same signal-driven shape `BruteProtectorReactionSystem` already
uses - and calls `ReviveUtility.Cancel` directly on whichever entity just took the hit, ending the
hold (the reviver has to press-and-hold again to resume) rather than freezing it in place for a
fixed window. Since `ReviveChannel` only ever exists on a reviver's own entity, `target` here is
always the **reviver**, never the person being revived (who is `Invulnerable` while Downed/KO -
`DamageUtility.ApplyDamage`'s `Invulnerable` gate returns before either signal would ever fire for
them). **Free emergent property, not extra code: a self-revive channel can never be
damage-interrupted**, since the self-reviver is by definition Downed (self-revive no longer applies
to KO either, see below) and therefore always Invulnerable - moot anyway, since self-revive is an
instant press/confirm, not a hold (see below).

**KO revival was ultimately removed rather than tuned further.** Even after the decay/interrupt
rework above, a full 5s KO hold remained fragile under sustained fire - the user's explicit call was
to cut it entirely (see "Life state" above) rather than keep chasing the number down. Everything in
this section (the channel, the decay, the interrupt-on-damage) now only ever applies to a Downed
target - `ReviveConfig.KOReviveDuration` no longer exists.

## Context Interaction extension

Three small, generic additions to the existing mechanism, all confirmed against its real source
before being made:

- `InteractableKind` gains `Revive`; `ContextInteractionState` gains `Occupied` (someone *else* is
  already reviving this target right now - deliberately not folded into `AlreadyUsed`, which means
  "you personally already consumed this POI forever," a different meaning entirely; same precedent
  that added `NotNeeded` for Healing Shrine once a second genuine need proved out).
- `ContextInteractionSystem`'s scan now skips `candidate == filter.Entity` (a player could otherwise
  match their own `Interactable{Kind=Revive}` tag) and early-outs entirely (`State = None`) for an
  incapacitated reviewing player - they can't resolve a normal interaction while incapacitated, and
  self-revive is an entirely separate `SelfReviveCommand` path (see below) that never touches this
  scan or `SkillSystem`'s redirect at all.
- **Kind-based priority.** The pre-existing `Interactable.Priority` field is only an exact-distance
  tie-break - verified by reading the scan's own `better` comparison. Making Revive always beat an
  ordinary POI regardless of distance needed one small, generic extra tier ahead of it:
  `InteractableKindUtility.GetPriorityTier(kind)` (Revive → 1, everything else → 0), folded into the
  scan as `(tier, distance, Priority)` instead of just `(distance, Priority)` - reusable by any
  future always-wins interactable, not a hardcoded `if (kind == Revive)` in the loop.

`ReviveUtility.ResolveInteractionState`/`TryBeginInteraction` mirror `HealingShrineUtility`'s shape
one-for-one. Revive has **no** `PoiAvailability`/`PoiUsagePolicy` concept - it has to work
identically whether the run is currently in Combat or Breathing, unlike every other Interactable
kind.

## Self-revive

Confirmed with the user: **every player carries their own independent `SelfReviveCharges`**
(`CharacterStats`, meta-progression-seeded). Mirrors the existing `RerollQuantity`/
`WeaponTalentLevel` talent pattern exactly (`PlayerTalents.SelfReviveCharges` → a new
`self_revive_charges` `PlayerPrefInt` in `MatchMakingConfig`, seeded once at spawn via
`PlayerSpawnUtility.Spawn`, inherits the same "nothing writes this pref yet" gap those two already
have).

**Only works while Downed - KO has no self-revive path either, same as it has no teammate-hold
path (see "Life state" above).** Originally usable whenever Downed or KO regardless of team
composition; once KO revival was cut entirely, `ReviveUtility.TryPerformSelfRevive` was narrowed to
check `State == Downed` specifically rather than the generic `IsIncapacitated`. A KO'd player's own
unspent charges simply sit unused - there's nothing left to spend them on until
`Global.BreathingAreaSecured` revives them for free (see below); `SelfReviveWidget` hides its own
charges readout and button entirely once KO'd rather than showing a permanently-disabled control
(see "View side" below).

**Self-revive is a deliberate single press/confirm, not a hold** - confirmed with the user
(revised from the original spec's "SELF REVIVE / Hold [Interact]" language once the UI moved into
its own dedicated window). `ReviveUtility.TryPerformSelfRevive` resolves instantly: no
`ReviveChannel`, no progress/duration, no `ReviveHolder`. It's triggered by a new zero-payload
`SelfReviveCommand`, sent from a dedicated `SelfReviveWidget` (View-side, see below) rather than the
Hero Skill button/proximity-redirect system at all - self-revive never touches `ContextInteraction`/
`Interactable`/`SkillSystem`'s redirect in any way. `PlayerLifeStateSystem` processes the command
(folded in there since it already iterates every player with `PlayerLifeState` each tick), fully
re-validating (`State == Downed`, charges left) before calling `PlayerLifeStateUtility.Revive(f,
player, player)` and decrementing `SelfReviveCharges`.

In co-op, self-revive and a teammate's hold-to-revive channel are **always simultaneously valid**
while Downed - the player can press SELF REVIVE at any point, or simply wait for a teammate; neither
path blocks or races the other. If a teammate is mid-channel and the target self-revives first,
`ReviveChannelSystem`'s own validity check (`target.State == Downed`) cancels that channel cleanly
next tick, same as any other "target became invalid" case.

## Auto-revive on Breathing area secured

An unconditional path back to Alive, layered on top of teammate-hold and self-revive (both Downed
-only now) - confirmed with the user after testing showed manual revival could stay effectively out
of reach for the whole remainder of a fight, and became **the only way back at all** once KO
revival was cut (see "Life state" above): **the instant `Global.BreathingAreaSecured` flips from
false to true, every still-Downed/KO player is fully revived automatically**, no hold, no charge
spent, no player action at all. Without this, a KO'd player with no teammate able to secure the area
would simply be stuck for the rest of the run (down to `RunFailed`, see below, if literally everyone
ends up in that state at once). `BreathingAreaSecured` (`GameState.qtn`) already existed before this
feature - it's
recomputed every tick by `SurvivalProgressionUtility.Tick` (`currentPhase.Kind ==
SurvivalPhaseKind.Breathing && encounterCleared`, i.e. the run has genuinely entered a Breathing
phase AND every last enemy is actually dead/Retired, not just "the phase boundary was crossed") and
already backs the pre-existing Breathing-only POI gate (`PoiAvailabilityUtility`). `Tick` now also
edge-detects its own false→true transition by comparing against the field's own previous-tick value
(no new field needed) and, on that exact tick only, calls a new
`PlayerLifeStateUtility.ReviveAllIncapacitated(f)`.

`ReviveAllIncapacitated` collects every entity carrying `PlayerLifeState` with `State != Alive` and
calls the exact same `Revive(f, target, reviver)` every other completion path funnels through (full
heal to `ReviveHealthPercent`, a fresh `ReviveInvulnerabilityDuration` window, `Interactable`
removed) - not a bespoke "just flip `State` back to `Alive`". `reviver` is passed as
`EntityRef.None` (nobody specific did this) - safe, since `HitFeedback` is the only other
`EventPlayerRevived` consumer and only reads `Target`. Deliberately does **not** manually cancel any
teammate's own in-progress `ReviveChannel` on one of these entities - `ReviveChannelSystem`'s own
very first validity check (`target.State == Alive`) already cancels a now-moot channel cleanly the
next tick, the same self-healing path every other "target became invalid mid-hold" case already
relies on (target died, disconnected, self-revived).

Deliberately lives in `PlayerLifeStateUtility` (life-state transitions), not in
`SurvivalProgressionUtility` itself (pacing only, per that file's own header comment) - the edge
DETECTION stays in `Tick` (it already owns computing the field every tick), the SIDE EFFECT is
delegated out to the file that actually owns Alive/Downed/KO transitions.

## Movement/weapon/skill locking - two distinct gates

`PoiInteractionLockUtility.IsInputLocked` gains one more OR clause (`f.Has<ReviveChannel>`) -
correctly full-locks a reviver's Weapon/Skill/new-interaction at those three call sites, same as
Cursed Rift/Store/Blacksmith already do. **`PlayerMovementProcessor.BeforeMove` is the one exception**
- confirmed with the spec (`ReviveMoveSpeedMultiplier = 0.30`, not a full stop), so that call site
carves `ReviveChannel` back OUT of the shared zero-speed branch and multiplies `targetSpeed` by
`ReviveConfig.ReviveMoveSpeedMultiplier` instead:

```csharp
if (Stunned || Rooted || incapacitated || (IsInputLocked && reviving == false))
    targetSpeed = FP._0;
else if (reviving)
    targetSpeed *= ReviveConfig.ReviveMoveSpeedMultiplier;
```

A Downed/KO player themselves is separately, fully pinned via `IsIncapacitated` - no partial
movement for the incapacitated player, only for an *Alive* reviver.

## Run-failure hook (`GameState.RunFailed`)

Confirmed with the user: add a minimal, vocabulary-only hook rather than nothing - `GameState`
gains a `RunFailed` value (same "wired later" precedent `GameState.Event`/pre-2026-08-17
`GameState.Boss` already established). `RunFailureSystem` fires
`GameStateUtility.SetState(f, GameState.RunFailed)` exactly once, the instant every connected,
spawned player is simultaneously not-`Alive` **and** nobody has any way back on their own. Updated
once KO revival was cut: a still-`Downed` player's unspent `SelfReviveCharges` still count as an
escape (they could press it any moment), but a `KO`'d player's charges no longer do - they have no
self-revive path at all anymore, so their own charge count is irrelevant to whether the run can
still recover. Concretely: the loop returns early (not failed) on the first player found `Alive`, or
`Downed` with `SelfReviveCharges > 0`; a `KO`'d player, or a `Downed` player with 0 charges, both
fall through as "no escape." Nothing downstream consumes `RunFailed` yet, deliberately -
`GameplaySystemGroup` is not disabled here, a future Game Over screen would add that itself.

## View side

- `ReviveInteractionPromptView` (new, on every hero View prefab) - `PoiView` can't be reused as-is:
  it only spawns its prompt once, from `Initialize()`, gated on `Has<Interactable>` at that single
  moment, correct for a POI that either has `Interactable` for its whole life or never does, wrong
  for a player who gains/loses it repeatedly across a match. This instead edge-detects the
  `Has<Interactable>` transition every `QUpdate` and spawns/despawns the same
  `InteractionPromptWidgetManager`-pooled widget accordingly - now correctly covers the KO dead end
  too, for free: `PlayerLifeStateUtility.EnterKO` removes `Interactable`, so this same edge-detect
  despawns the prompt the instant KO hits (nothing left to interact with), no special-casing needed
  here.
- `InteractionPromptWidget` gains a `progressFillSlider` field (optional, off if unassigned - a
  `Slider`, not an `Image.fillAmount`, confirmed with the user; the earlier `pausedIndicator`
  companion field was removed once damage started interrupting a hold outright instead of merely
  pausing it - see above, there's no more "temporarily frozen, about to auto-resume" state left to
  show). **Explicitly hidden everywhere except while a real Revive channel is actively
  progressing** (confirmed with the user) - a real gap, not a hypothetical one: this exact same
  widget/prefab is shared across every `Interactable` kind via `InteractionPromptWidgetManager`'s
  own single-`widgetPrefab` pool (Cursed Rift/Healing Shrine/Store/Blacksmith/Traversal Challenge/
  Revive alike), so without an explicit hide a non-revive instance would show these elements at
  whatever state the shared prefab happened to be left in. Hidden in `Setup` (before anything else
  runs), in `RefreshReviveTitle`'s early-return (runs every frame for every widget regardless of
  kind), and in `UpdateFromReviveState`'s own idle branch (Downed but nobody local currently
  holding - no stray empty bar while just standing nearby); shown only in that same method's
  active-channel branch. **Now Downed-only throughout** - the earlier REVIVE/RESTORE title flip and
  `koTitleColor` were both removed once KO revival was cut: this whole widget never exists for a
  KO'd entity in the first place (despawned by `ReviveInteractionPromptView`'s own edge-detect the
  instant `Interactable` is removed, see above), so there's no KO state left for it to display.
  `RefreshReviveTitle` now checks `State == Downed` specifically (not just `!= Alive`) and always
  sets the title to plain "REVIVE" - the same "never trust it, re-validate" discipline as everywhere
  else, even though a live KO case should be unreachable. **`downedTitleColor` was removed too**
  (confirmed with the user, once it was the only state left to color) - `titleText` now keeps
  whatever color is authored on the prefab itself, no runtime override at all. A `SetTitle` that
  only writes on change. The bleed-out countdown reuses the existing `descriptionText`/
  `ApplyDescription` mechanism (a `_bleedOutDescription` string field) rather than a dedicated
  `TMP_Text`, taking priority over the plain per-`ContextInteractionState` description whenever
  it's non-empty - both in the generic idle path (`UpdateFromState`) and while actively being
  revived (`UpdateFromReviveState`, alongside the progress bar, reinforcing that it's currently
  frozen). Reads the live value directly, so it automatically reflects the simulation's own
  pause-while-held behavior (`PlayerLifeStateSystem`) with no extra UI logic. A separate
  `UpdateFromReviveState` branch, checked *before* the generic `ContextInteraction`-driven switch,
  takes over the display (progress fill only) while one of this client's own local players is
  actively holding the channel; otherwise it hides the fill and falls through to the generic path,
  which already handles the passive Available/Occupied display correctly using the now-fresh
  title/color/countdown.
- `SkillCooldownUiWidget` (HeroSkill slot only) gains one new branch ahead of the existing Context
  Interaction redirect, reusing `cooldownFillImages` for hold-progress while a TEAMMATE `ReviveChannel`
  is present (needed because `ContextInteraction.State` reads `Busy`, not `Available`, once a channel
  begins - the existing branch alone would stop showing anything mid-hold). Its own duration lookup
  (`ResolveReviveDuration`) reads `ReviveConfig.DownedReviveDuration` unconditionally now, no `Kind`
  switch left to mirror. Never shows anything for self-revive - that lives entirely in
  `SelfReviveWidget` instead.
- `SelfReviveWidget` (new, `Assets/_Project/Scripts/UI/InGame/Hud/`) - a dedicated small HUD
  element, content-wise closer in spirit to `BossWindow` than to `ChooseWindow` (deliberately not
  the latter - confirmed with the user, avoiding this codebase's own established "don't build a
  second parallel window" anti-pattern by not touching `ChooseWindow` at all rather than cloning
  it). Architecturally it's a **Widget, not a Window** - a self-polling `QuantumGlobalMonoBehaviour`
  (same shape as `SkillCooldownUiWidget`/`CurrencyUiWidget`), not a `UiWindow` subclass like
  `BossWindow`/`ChooseWindow` - those are thin presentation shells driven externally by a separate
  poller (`BossWidget`, for `BossWindow`); `UiWindow`'s static `Instance` singleton field also
  wouldn't support 2 simultaneous per-local-slot instances cleanly. Self-binds to a local player
  slot (`localSlotIndex`/`autoBindLocalSlot`, same `MyLocalPlayer.Instance.BindToSlot` convention
  `SkillCooldownUiWidget`/`CurrencyUiWidget` already use - a second scene instance with
  `localSlotIndex = 1` covers couch co-op's second local player). Shown whenever that local player's
  own entity is incapacitated (Downed OR KO - unlike the button/charges below, the widget itself
  still appears for both, so a KO'd player at least sees their own state clearly). Title still
  reads "YOU ARE `<color=#FD3971>`DOWNED`</color>`"/"YOU ARE `<color=#FD3971>`KO'D`</color>`"
  (`titleHighlightColorHex`, only the state word colored) regardless of the revive-path change
  below - a KO'd player still needs to know they're KO'd, they just can't do anything about it
  themselves anymore. **`chargesText`/`selfReviveButton` are now hidden entirely while KO**
  (`gameObject.SetActive(isKo == false)`, not just `interactable = false`) - confirmed with the
  user's "remove KO revive completely" decision: there's nothing left to press or spend once KO'd,
  so showing a permanently-dead button/charge count would be actively misleading, unlike
  `HealingShrineUtility`'s own "let the press fail loudly rather than hide the button" precedent
  (that precedent is for a press that's merely pointless *right now*, not permanently unusable for
  the rest of this life state). Optional `bleedOutTimerText` (Downed-only, unaffected by this
  change) treatment otherwise matches `InteractionPromptWidget` above, kept in sync by hand since
  they're two independent View classes reading the same `PlayerLifeState` fields, not a shared
  base.
- **Character collapse/topple pose** (`BlobAnimationView`, the player rig's own procedural
  squash/stretch animator - see "Environment Details"/this project's animation is 100% procedural,
  no Animator Controller or animation clips exist for any hero) gains a new `State.Downed`,
  confirmed with the user as the priority feedback piece (over a screen tint, an impact
  shake/flash, or camera lock, all considered and deferred). Directly ports
  `EnemyBlobAnimationView`'s **Burrow** shape (reversible topple/squash/settle via a 0→1 `_downedT`
  eased with `Mathf.MoveTowards` over `downedFallDuration`/`downedRiseDuration`), not its **Die**
  shape (one-way, since a dying enemy is actually destroyed after - a Downed/KO player has to
  un-topple cleanly on revive). Edge-triggered off `PlayerLifeState.State` every `QUpdate`, the same
  way `EnemyBlobAnimationView` edge-detects `Burrowed`; while falling/held/rising it fully
  overrides the normal KCC-velocity-driven Idle/Run/Air locomotion below it in `QUpdate` (a
  Downed/KO player can't move anyway). Legs/skateboard are simply relaxed to their normal resting
  pose (`RelaxLegs`/`ApplyLegAngle(0f)`, same as Idle/Anticipate) rather than a separate, untested
  splayed-leg pose - the root-level topple alone reads as "fallen over." `ApplyPose` gained an
  optional `depthOffset` parameter (default 0, every existing call site unaffected) mirroring the
  enemy sibling's own `depthOffset`, for `downedGroundOffsetZ`. `[Button]`-tagged
  `TriggerDowned`/`TriggerRevived` mirror `EnemyBlobAnimationView.TriggerBurrowDown/Up`'s own
  Play-Mode preview convenience - gated on `_downedFalling` (not `isIncapacitated` read fresh from
  the frame each tick) specifically so these buttons work standalone without a live simulation
  driving a real `PlayerLifeState`.
- **Weapon hidden while incapacitated** (`WeaponViewController.QUpdate`) - a collapsed character
  still visibly holding a raised weapon read as broken, confirmed with the user. Toggles the
  spawned `WeaponView` prefab's own `GameObject` active state off `PlayerLifeStateUtility.
  IsIncapacitated`, restored the instant they're revived. No new fields - reuses the existing
  `currentWeaponView` reference `SpawnWeaponView` already tracks.
- `PlayerDowned`/`PlayerKO` events (`Events.qtn`) - vocabulary only, nothing consumes them yet.
  `PlayerRevived` is NOT vocabulary-only, though - `HitFeedback` (`Assets/_QuantumUser/View/Util/`)
  subscribes to it. Real bug found and fixed: `DamageUtility.ApplyDamage` fires `EntityDied`
  **unconditionally** at the very top of its lethal-damage branch, before it even checks whether
  the target is a player going Downed rather than truly dying - so `HitFeedback.OnEntityDied`'s
  `Die()` (a permanent gray tint + a `QUpdate` lockout, previously undone by the old
  `RespawnPlayer`'s own `PlayerRespawned` event) fires for a Downed player exactly like it would
  for a dead enemy, and nothing undid it anymore once `RespawnPlayer` was deleted. `HitFeedback` now
  also subscribes to `PlayerRevived` and calls the same `Respawn()` the fall-recovery path
  (`PlayerRespawned`) already used, clearing the tint the instant a hold-to-revive or self-revive
  actually completes.

  **Related, NOT yet fixed** - the same unconditional `EntityDied` branch also unconditionally
  calls `ExperienceUtility`/`ScrapUtility`/`RiftShardUtility`/`CoinUtility.TrySpawnDrop` before the
  Enemy/PlayerLink/prop split, meaning a player merely going Downed currently drops Exp/Scrap/Rift
  Shard/Coin orbs exactly as if they'd actually died. Almost certainly not intended, but out of
  scope for this pass since it wasn't part of what was asked - flagged here rather than silently
  fixed.

## Mobile / controller

No new input code anywhere - confirmed by reading `QuantumDebugInput.cs`: every platform already
collapses onto the single unified `Input.HeroSkill` Quantum button via the Control Freak 2 plugin
(`CF2Input.GetButton("Skill") || CF2Input.GetKey(KeyCode.E)`) before it ever reaches simulation code.
`ReviveChannelSystem` reading `Input.HeroSkill.IsDown` therefore works identically on PC/gamepad/
mobile for free, for the teammate-revive hold. Self-revive doesn't even need this - `SelfReviveWidget`
is a plain UI `Button`, which already works identically across mouse/touch/gamepad-cursor-navigation
with zero platform-specific code. There is no per-platform button-glyph/icon system anywhere in this
codebase to extend (confirmed via repo-wide search) - the existing single-hand-assigned-`Sprite`-per-
widget convention (`contextInteractionIcon`) is the correct, sufficient pattern to reuse as-is;
building a new glyph system was explicitly out of scope.

## Known simplifications

- `ReviveProgressDecayRate` (0.5, half the build rate) is a decisive placeholder, not a tuned
  balance value - needs a real pass once actual co-op combat pacing is testable at scale.
- The collapse pose's exact numbers (`downedFallDuration`/`downedToppleDegrees`/`downedSquash`/etc.)
  are decisive placeholders, same as every other `BlobAnimationView` tuning group - needs a real
  pass once actual sprite art exists per hero.
- No crawl movement for the Downed/KO player themselves - full incapacitation (matches the spec,
  which only discusses the *reviver's* reduced movement, never the target's).
- `GameState.RunFailed` is vocabulary-only - nothing currently transitions the match out of it, ends
  the run, or shows any UI.
- **KO is a deliberate, permanent dead end until the area is secured - not a bug, not a gap.**
  Confirmed explicitly with the user ("remove KO revive functionality completely"): a KO'd player
  cannot be revived by a teammate's hold or their own charges, full stop, regardless of how long the
  fight drags on - `Global.BreathingAreaSecured` is the only way back. In solo play, or if every
  connected player ends up KO'd simultaneously, this can mean an extended stretch (or, in the worst
  case, the rest of a Combat phase) with nobody able to act at all before the area finally clears -
  mitigated only by the pre-existing `EnemyLifecycleSystem` Irrelevant→Retired timeout (enemies with
  nobody left to fight eventually auto-retire, satisfying `IsEncounterCleared` even with zero kills),
  the same safety net an Elite/Boss encounter already relies on to avoid a true deadlock.

## Short version

The code compiles once codegen picks up every changed/new `.qtn` file (`PlayerLifeState.qtn`,
`Poi/Revive.qtn` - most recently the `ReviveTargetKind` enum/`ReviveChannel.Kind` field removal once
KO revival was cut, see "Life state" above - `ContextInteraction.qtn`'s new `Revive`/`Occupied`
values, `StatusEffects.qtn`'s `ReviveImmunityRemaining`, `GameState.qtn`'s `RunFailed`,
`CharacterStats.qtn`'s `SelfReviveCharges`, `Events.qtn`'s 3 new events), and `SystemSetup.User.cs`
registers `PlayerLifeStateSystem`/`ReviveChannelSystem`/`ReviveDamageInterruptSystem`/
`RunFailureSystem` right after `SkillSystem`; `CommandSetup.User.cs` registers the new
`SelfReviveCommand`.
`DamageUtility.RespawnPlayer` (the old instant-respawn behavior) is deleted entirely - a genuine,
deliberate replacement of existing behavior, not purely additive. Auto-revive-on-secure
(`PlayerLifeStateUtility.ReviveAllIncapacitated`, called from `SurvivalProgressionUtility.Tick`)
needs no new `.qtn`/config/Editor authoring at all - it reuses `GameState.qtn`'s pre-existing
`BreathingAreaSecured` field and `ReviveConfig`'s existing heal/invuln values.

`Tools/RiftRaiders/Generate Revive Content` authors `ReviveConfig.asset` with tuned defaults - not
yet run. Still needed by hand before anything works end-to-end at runtime:
1. Assign `RuntimeConfig.ReviveConfig` (`QuantumMenuConfig.asset`), same place every other config
   is already assigned.
2. Add a `PlayerLifeState` component to every hero `EntityPrototype` (`Pixie`/`Brute`/`Zara`/`Kai`/
   `Max`/`MainChar`/`Lux` under `Assets/_QuantumUser/Entities/Characters/`) - defaults all-zero
   (Alive), nothing else to author on it.
3. Add `ReviveInteractionPromptView` to each hero's own View prefab, wiring
   `InteractionPromptWidgetManager` the same way `PoiView` already is.
4. Build a `Slider` on the HUD prompt prefab for `InteractionPromptWidget`'s new
   `progressFillSlider` field (the title is plain "REVIVE" text now, no color override; the bleed-out
   countdown itself reuses the existing `descriptionText`, no new Text element needed - only needs
   Inspector overrides if a different look is wanted).
5. Wire `SkillCooldownUiWidget`'s HeroSkill-slot `contextInteractionIcon`/`interactPromptRoot`
   (same pre-existing gap `docs/breathing-poi.md` already tracks) - teammate revive only.
6. Build a `SelfReviveWidget` prefab (`titleText`/`chargesText`/`selfReviveButton`/
   `bleedOutTimerText`) per local player slot in the HUD scene (`localSlotIndex` 0 and 1 for couch
   co-op) - entirely unauthored today.
7. Nothing writes the new `self_revive_charges` `PlayerPref` yet - same accepted gap
   `weapon_talent_level`/`reroll_quantity` already have; seed `PlayerTalents.SelfReviveCharges` by
   hand in the Inspector for testing.

Not yet manually verified end-to-end in-Editor, solo or co-op.
