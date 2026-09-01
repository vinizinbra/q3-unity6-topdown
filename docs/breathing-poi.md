# Breathing POIs (Healing Shrine, Cursed Rift)

The first two Breathing-only world POIs - **Healing Shrine** (press-to-heal, one-shot, no Choice
Window) and **Cursed Rift** (a deliberate two-step choice: sacrifice something now for a Rift
Mutation reward, opens a full Choice Window). Read `docs/run-phase.md` first for the Combat/
Breathing state machine both POIs gate on, and `docs/choice-window-refactor.md` for the Choice
Window Cursed Rift opens. This doc covers what's specific to the two POIs themselves, the generic
POI availability/usage infrastructure they share, and the generic Context Interaction /
Base-Skill-button redirect both POIs now use (Healing Shrine was originally a pure walk-in
auto-heal with no interact button at all - reworked into press-to-heal specifically so its own
"you can use this" moment is explicit and player-driven, same as Cursed Rift, rather than a passive
side effect of standing still).

## Generic POI availability/usage infrastructure (`Poi.qtn`)

Deliberately small, shared by both POIs (and meant for future ones - Store, Upgrade Station,
Artifact Pedestal, Rift Node - none implemented yet):

```
struct PoiAvailability
{
    Boolean AvailableInCombat;
    Boolean AvailableInBreathing;
}

enum PoiUsagePolicy : Byte { Reusable, OncePerPlayerPerBreak, OncePerPlayerPerRun, OncePerWorld, Cooldown }

struct PoiUsageEntry { EntityRef Poi; Int32 UsedAtBreathingIndex; FP CooldownRemaining; }

component PoiUsage { array<PoiUsageEntry>[8] Entries; } // per-player
```

- `PoiAvailabilityUtility.IsAvailable(f, availability)` maps `Survival→AvailableInCombat`,
  `Breathing→AvailableInBreathing`, every other `GameState` → `false` - a Shrine/Rift correctly
  goes dormant the instant an Upgrade screen opens mid-Breathing, not just on a Combat/Breathing
  edge. As of 2026-08-16, the `Breathing` case is additionally gated on `Global.
  BreathingAreaSecured` (`AvailableInBreathing && BreathingAreaSecured`) - a POI stays unavailable
  through the whole "area not secured yet" window (see `docs/run-phase.md`'s Elite/Boss/Breathing
  encounter-clear hold), not just from the phase boundary onward. Since this is the single funnel
  every POI resolver (`HealingShrineUtility`/`CursedRiftUtility`/`StoreUtility`/`BlacksmithUtility`)
  and `PoiActivationUtility.Refresh` already call, no other file needed touching.
- `PoiUsageUtility.CanUse`/`MarkUsed(f, player, poi, policy, cooldownDuration = 0)` - per-player,
  keyed by the POI's own `EntityRef`, same fixed-array-of-keyed-entries convention
  `RiftMutationPicks`/`GlobalUpgradePicks` already use. `OncePerPlayerPerRun` stores a `-1`
  sentinel ("used forever"). **`OncePerWorld` is declared but not implemented this pass** (`CanUse`
  logs an error and returns `false`) - no current POI needs it; it would need a flag on the POI's
  own component instead of this per-player one.
- **`Cooldown`** (2026-08-29) - per-player like `OncePerPlayerPerBreak`/`PerRun`, but real-time
  instead of Break-indexed: usable again once `PoiUsageEntry.CooldownRemaining` decays to 0.
  `cooldownDuration` is NOT part of the generic policy vocabulary - it's a per-POI-kind tuning
  value living on the POI's own component (e.g. `HealingShrine.CooldownDuration`, same convention
  `HealPercent` already uses), forwarded into `MarkUsed` by whichever call site owns it. Ticked
  down every frame by a new `PoiUsageUtility.TickCooldowns(f, player)`, called once per
  `PoiUsage`-carrying entity per tick from `PoiActivationSystem` (the existing generic per-tick
  POI-infra pass, extended with one more loop - this one keyed by player, not by POI). Deliberately
  ticks by `f.DeltaTime` rather than comparing against a stored timestamp, since it has to keep
  counting down through a Breathing Break even though `Global.SurvivalTime` itself freezes there
  (see `docs/run-phase.md`'s "Independent timers") - a timestamp-based cooldown would silently
  pause too. Meant to pair with `PoiAvailability.AvailableInCombat = true` so a POI can be an
  anytime tool with a real-time cost, not just a once-per-Break pick - Healing Shrine is the first
  candidate (see below), authored per-instance in the Editor like every other POI field.
- **8-slot array is scoped to "2 POI instances exist in the whole run today"** (both persistent,
  singular) - grow it (same pattern `RiftMutationPicks` went `[16]`→`[32]`) once more POIs land.

### Shared View state (`PoiActivation`/`PoiViewState`)

A third, generic piece shared by both POIs' View code:

```
enum PoiViewState : Byte { Inactive, Active, Expired }

component PoiActivation { PoiViewState State; }
```

- **`Inactive`** - not currently available (Combat, or any other non-Breathing state).
- **`Active`** - available AND at least one CONNECTED player can still use it.
- **`Expired`** - available, but every connected player has already used it up (e.g. still
  Breathing, but you personally already used this Shrine this Break).

`PoiActivationUtility.Refresh(f, poi, availability, usagePolicy)` resolves this - `Inactive` if
`PoiAvailabilityUtility.IsAvailable` is false, otherwise `Active`/`Expired` depending on whether
ANY connected player (a real, deterministic, every-client-agrees-on fact - not a per-viewer
approximation) still passes `PoiUsageUtility.CanUse`. A new `PoiActivationSystem` (unfiltered
`SystemMainThread`, registered after both `SkillSystem` and `CursedRiftSystem` so a usage marked by
either this same tick is already reflected) refreshes every `HealingShrine`/`CursedRift` entity's
own `PoiActivation.State` each tick - deliberately a genuine simulation-side component that View
code reads directly, not something each View re-derives from `PoiAvailability`/`PoiUsage`
independently. `HealingShrineUtility`/`CursedRiftUtility` never touch `PoiActivation` themselves -
"is this usable" (gameplay) and "what should it look like" (presentation) stay cleanly separate.

## Healing Shrine

Persistent world POI. Press-to-heal via the same generic Base-Skill-button redirect Cursed Rift
uses (see "Context Interaction" below) - walking into range does **not** auto-heal; the player
must press the button, same as Cursed Rift, just resolved in a single tick instead of opening a
Choice Window. (Originally a pure walk-in auto-heal with no button at all - reworked so the moment
of "you just got healed" is an explicit, player-driven press rather than a passive side effect of
standing still, and so both POIs share one interaction model instead of two.)

```
component HealingShrine
{
    PoiAvailability Availability;
    PoiUsagePolicy UsagePolicy;
    FP HealPercent;
    FP CooldownDuration; // only read under UsagePolicy == Cooldown - see the Cooldown bullet above
    // Radius/Priority live on the sibling Interactable component instead - one source of truth,
    // same convention CursedRift.qtn already uses.
}
```

No system of its own - `HealingShrineUtility.ResolveInteractionState`/`TryInteract` are called
directly from `ContextInteractionSystem`'s per-kind switch and `SkillSystem`'s own per-kind
dispatch on press (see "Context Interaction" below), exactly the same call shape
`CursedRiftUtility.ResolveInteractionState`/`TryBeginInteraction` already use - the only difference
is `TryInteract` heals and marks used immediately, in the same tick as the press, rather than
opening a multi-step interaction. `ResolveInteractionState`: `PhaseUnavailable` if
`PoiAvailabilityUtility.IsAvailable` is false, `AlreadyUsed` if `PoiUsageUtility.CanUse` is false,
`NotNeeded` if the player's own `Health.CurrentHealth >= Health.MaxHealth` (checked last, after
`AlreadyUsed` - a player who's both already used it AND full should read as "already used," the
more permanent reason, not "full health," which would misleadingly flip back to `Available` the
instant they take any damage), else `Available`. `TryInteract` re-validates that in full (never
trusts the View/target resolution alone) then calls `HealUtility.ApplyHeal(f, player, player,
HealPercent)` → `PoiUsageUtility.MarkUsed`. Never touches Shield (`HealUtility` only ever touches
`Health`). While `NotNeeded`, the Base Skill press still redirects here rather than falling through
to a normal Hero Skill cast (`SkillSystem`'s own gate lets `Available` OR `NotNeeded` through - a
player pressing the button at a full-Health Shrine is clearly trying to interact, not cast their
skill) - `TryInteract` fires `EventContextInteractionRejected(player, shrine)` instead of healing,
which `InteractionPromptWidget` uses to pop a `ToastManager` toast with that same
`PoiView.promptNotNeededDescription` (default "FULL HEALTH") - see "View side" below. Deliberately
PRESS-triggered, not proximity-triggered - the toast only fires on an actual attempted
interaction, never just from standing near the Shrine (the passive world-space label already
covers that, more quietly).
Visual state is driven by the shared `PoiActivation.State` (see above) - the Shrine reads
`Active`/stays visually usable while ANY connected player could still use it, not just the local
one(s), and only shows `Expired` once every connected player has used it up this Break.

## Cursed Rift

Persistent world POI. A deliberate two-step choice, so entering radius does **not** auto-trigger -
the player must press the (redirected) Base Skill button. Never pauses anything - not the
simulation, not `Time.timeScale`, not the Breathing timer, not other players. Only this one
player's own movement/weapon/skill input is locked while their window is open.

```
component CursedRift
{
    PoiAvailability Availability;
    PoiUsagePolicy UsagePolicy;
    // Radius/Priority live on the sibling Interactable component instead - one source of truth.
}

enum CursedRiftInteractionState : Byte { SelectingSacrifice, SelectingMutation }

component CursedRiftInteraction
{
    EntityRef Rift;
    CursedRiftInteractionState State;
    array<AssetRef<SacrificeDefinition>>[3] SacrificeChoices;
    Byte SacrificeChoiceCount;
    array<LevelUpOption>[3] MutationChoices; // same shape as LevelUpChoice.Options, deliberately
    Byte MutationChoiceCount;
}
```

Per-player, found via `PlayerLink`, same "component presence = interaction in progress, absence =
completed" convention `LevelUpChoice` already uses - but **deliberately NOT `LevelUpChoice`
itself**, and never touches `Global.LevelUpScreenOpen`/`GameState.Upgrade`/
`SystemDisable<GameplaySystemGroup>`.

### Flow

No separate confirm step - clicking a sacrifice card commits immediately (applies its cost), same
"one click = one irreversible pick" idiom every other Choice Window screen (Level-Up, Choose
Weapon) already uses. `docs/choice-window-refactor.md` covers WHY (an earlier design had a
`ConfirmingSacrifice` state + a bespoke 2-button confirm sub-panel; both were removed once the user
flagged the confirm step as unnecessary new UI - Cursed Rift's screen is just two back-to-back uses
of the same `ChooseWindow` instance Level-Up already has).

```
Press Base Skill (redirected, see Context Interaction below)
  -> CursedRiftUtility.TryBeginInteraction
     re-validates (availability/radius/usage) fully in Quantum, rolls up to
     CursedRiftConfig.SacrificeChoiceCount eligible sacrifices -> SelectingSacrifice
  -> SelectSacrificeCommand{OptionIndex} -> SacrificeDefinition.ApplyCost,
     LevelUpUtility.RollMutationOptions(...) -> SelectingMutation
  -> SelectMutationCommand{OptionIndex} -> RiftMutationUtility.Grant (100% reused),
     PoiUsageUtility.MarkUsed, component removed (= Completed)

CancelCursedRiftCommand - only meaningful pre-payment (SelectingSacrifice): cancels entirely
                          (component removed). A no-op once SelectingMutation (irreversible past
                          that point - SelectSacrifice above already applied the cost by then).
```

`CursedRiftUtility.RollSacrificeOptions` is a small weighted-draw-without-replacement
implementation (same shape as `LevelUpUtility`'s own, kept separate since
`AssetRef<SacrificeDefinition>` isn't a `LevelUpOption`) filtered by each `SacrificeDefinition
.IsEligible` - fills fewer than `SacrificeChoiceCount` cards if not enough sacrifices are eligible
(e.g. a player with 0 Coins never sees Coin Offering).

**Mutation reward reuses the exact existing pipeline** - `LevelUpUtility.RollMutationOptions(f,
entity, config, count)` (a small additive extraction: the weighted-draw loop that used to be
inlined in `RollOptionsFor` is now a shared `DrawWeighted` helper, zero behavior change for
existing categories) calls the same `CollectRiftMutationCandidates` a normal level-up's
`RiftMutation` category already uses, and respects `CharacterStats.AllOrNothingActive` the same
way (rarity-shifted, collapsed to 1 choice) for consistency. `RiftMutationUtility.Grant` applies
the pick unchanged - no mutation logic duplicated anywhere.

### `CursedRiftSystem` - why it's NOT gated like `LevelUpSystem`

`LevelUpSystem` gates its whole `Update` on `Global.LevelUpScreenOpen` because it's the thing that
disables `GameplaySystemGroup`. `CursedRiftSystem` never disables anything, so it has no
re-entrancy hazard to guard against by living outside the group or behind a flag - it's registered
**inside** `GameplaySystemGroup` (right after `RiftMutationSystem`) and processes every
connected player's own command every tick, unconditionally. This is what makes "Situation B" work
for free: a Breathing Break ending mid-`SelectingMutation` (payment already committed) doesn't
strand the player - `RunPhaseUtility.CancelUncommittedCursedRiftInteractions` (called from
`CombatDirectorSystem` on the Breathing→Survival edge) only removes **uncommitted**
interactions (see `docs/run-phase.md`), so a committed one just keeps being processed by
`CursedRiftSystem` regardless of `CurrentState`, exactly as required.

### Per-player input lock (NOT a pause)

`CursedRiftUtility.IsInputLocked(f, entity)` (presence of `CursedRiftInteraction`) is checked from
three places, the exact same per-entity gate pattern `StatusEffectUtility.IsStunned`/`IsRooted`
already use for Stun:

- `PlayerMovementProcessor.BeforeMove` - `targetSpeed = 0` alongside the existing Stun/Root check.
- `WeaponSystem.Update` - early-return alongside the existing Stun check.
- `SkillSystem.Update` - both `DashSkill`/`HeroSkill` buttons are neutralized (passed as `default`
  into `UpdateSlot`, not skipped) so cooldown/stack-recovery ticking keeps running normally, only
  the press edge is blocked.

This is deliberately **not** `GameplaySystemGroup`/`Time.timeScale` - only the one interacting
player's own input is gated; the simulation, other players, and the Breathing timer all keep
running untouched.

## Context Interaction / Base-Skill redirect (`ContextInteraction.qtn`)

Generic "redirect the Base Skill button to a nearby world interaction" mechanism - the first
interact-button/prompt pattern in this codebase (previously: none, every pickup was pure
auto-collect, see `docs/chests.md`). Built generic on purpose, and already proven by a second real
user - Healing Shrine (one-shot, resolves same-tick) alongside Cursed Rift (opens a multi-step
Choice Window) - so a future third interactable POI (Store, Upgrade Station, Artifact Pedestal,
Rift Beacon, Challenge Totem) reuses the same redirect without a new permanent HUD button:

```
enum InteractableKind : Byte { CursedRift, HealingShrine }

// Richer than a plain found/not-found bool - lets the world-space prompt explain WHY a nearby
// Interactable can't be used right now (see InteractionPromptWidget), not just hide silently.
// SkillSystem's own redirect claims the press on Available OR NotNeeded (a deliberate, real
// attempt that's allowed to fail loudly via EventContextInteractionRejected - see below);
// PhaseUnavailable/AlreadyUsed/Busy all fall through to a normal Hero Skill press instead.
enum ContextInteractionState : Byte
{
    None, Available, PhaseUnavailable, AlreadyUsed, NotNeeded, Busy
}

component Interactable { InteractableKind Kind; FP Radius; Int32 Priority; }  // on POI entities

// Per-player. ActiveTarget is the CLOSEST in-range Interactable regardless of State - only
// State == Available means the Base Skill button actually redirects.
component ContextInteraction { EntityRef ActiveTarget; InteractableKind ActiveKind; ContextInteractionState State; }
```

`ContextInteractionSystem` (registered immediately before `SkillSystem`, after `KCCSystem` so it
reads this tick's resolved position) resolves each player's `ContextInteraction` fresh every tick -
one generic `f.Filter<Interactable, Transform3D>()` scan, closest-in-radius wins, `Priority` only
breaks an exact distance tie, the filter's own deterministic enumeration order is the final
tie-break beyond that (no explicit `EntityRef` compare needed - same convention
`EnemyMovementUtility`'s own nearest-target resolvers already rely on). **Target resolution is
purely geometric and does NOT filter by eligibility** - the world-space prompt needs to know about
a nearby-but-not-usable POI too (e.g. to show "come back on Break"), not just a fully-eligible one.
`State` is resolved separately, once, for whichever candidate wins that scan: a single top-level
`f.Has<CursedRiftInteraction>(player)` check (uniform across every `InteractableKind`, not
per-kind - Healing Shrine has no interaction component of its own to check, since its own
`TryInteract` never leaves anything persistent behind) forces `Busy` if the player is already mid a
different interaction; otherwise a small per-`Kind` switch (`CursedRift` →
`CursedRiftUtility.ResolveInteractionState`, `HealingShrine` →
`HealingShrineUtility.ResolveInteractionState`) resolves `PhaseUnavailable`/`AlreadyUsed`/
`Available` - the pragmatic middle ground given Quantum's qtn DSL has no polymorphic component
dispatch.

`SkillSystem.Update` checks `State == Available` (not just "something is nearby") before its
normal Hero Skill press-handling: if so and `HeroSkill.WasPressed`, it switches on
`ContextInteraction.ActiveKind` and calls whichever utility owns that interaction
(`CursedRiftUtility.TryBeginInteraction` opens the Choice Window;
`HealingShrineUtility.TryInteract` heals and marks used immediately, same tick), neutralizing the
button for that tick either way (a real skill cast never also fires on the same press) instead of
sending a `DeterministicCommand` - deliberate, since this reuses the exact same
polled-input+`WasPressed` mechanism every other skill activation already uses (the same category of
action, not a menu-confirmation like `SelectSacrificeCommand`). Target validity is always
re-checked fully in Quantum by each utility's own resolver (`CursedRiftUtility.CanInteract`
independently re-derives `ResolveInteractionState` AND re-checks Busy so it stays a safe standalone
entry point on its own; `HealingShrineUtility.TryInteract` re-checks `ResolveInteractionState`
itself before touching `Health`) - never trusted from the View/proximity alone.

`InteractionIcon`/`InteractionPrompt` text deliberately do **not** live on the qtn `Interactable`
component (simulation state can't hold a `Sprite`/arbitrary string content) - authored View-side
per POI instance instead (see `PoiView`'s own `promptTitle`/`promptActiveDescription`/
`promptPhaseUnavailableDescription`/`promptAlreadyUsedDescription` fields, below).

## View side

- **`SkillCooldownUiWidget`** (HeroSkill-slot instance only) - when `ContextInteraction.State ==
  Available` (not merely "something is nearby" - a `PhaseUnavailable`/`AlreadyUsed` target doesn't
  swap the button, only the world-space prompt reacts to those), swaps its icon to a new
  `contextInteractionIcon` field and toggles an `interactPromptRoot`, suppressing the normal
  cooldown fill - "my normal button is temporarily being used to interact with this object," same
  button position, obviously different icon. The DashSkill instance of this same widget never
  checks `ContextInteraction` at all.
- **`PoiView`** (entity-side, `Assets/_QuantumUser/View/Entities/Poi/PoiView.cs`) - a
  `CustomQuantumEntityViewComponent` on the POI's own view prefab, GENERIC across POI kinds (has
  no idea which one it's on): toggles Inactive/Active/Expired visuals off the shared
  `PoiActivation.State` (see above), and separately - from its `Initialize`/`DeInitialize`
  overrides - registers/deregisters this entity with `InteractionPromptWidgetManager` (only if the
  entity actually carries an `Interactable` component - both Healing Shrine and Cursed Rift do now),
  passing it one constant authored title (`promptTitle`, e.g. "CURSED RIFT"/"HEALING SHRINE") plus
  four authored per-instance OPTIONAL description strings (`promptActiveDescription`/
  `promptPhaseUnavailableDescription`/`promptAlreadyUsedDescription`/`promptNotNeededDescription`,
  e.g. "" / "COME BACK ON BREAK" / "ALREADY USED" / "FULL HEALTH" - `Available`'s own description
  defaults empty since the Base Skill icon swap already communicates "press to interact" on its
  own) - not hardcoded, so a future third Interactable POI reuses both components unchanged.
- **`InteractionPromptWidget`/`InteractionPromptWidgetManager`** (HUD-side,
  `Assets/_Project/Scripts/UI/InGame/Hud/`) - the world-space prompt itself. Deliberately **not** a
  `CustomQuantumEntityViewComponent` living in the entity's own 3D view hierarchy - it's UI, so it
  follows the same manager-pool pattern `CharacterUiWidget`/`EnemyUiWidgetManager`/
  `SentryUiWidgetManager` already establish: a plain `MonoBehaviour` widget spawned under the HUD
  Canvas (not the entity's own view prefab, which would put it inside whatever world rig hierarchy
  gets squashed/rotated for animation), tracked by a `Dictionary<EntityRef, InteractionPromptWidget>`
  in the manager, following the entity via a plain Unity `Transform` reference passed once at spawn
  (`UIHelper.TryWorldToAnchoredPosition`, same world-to-UI projection `DamageNumberUiWidget` uses)
  rather than re-deriving position from the Quantum frame every tick. Spawned/despawned once for
  the POI's whole lifetime (by `PoiView`, mirroring how `EnemyView`/`CharView`/`SentryView` spawn
  their own `CharacterUiWidget`); the widget then re-reads whichever LOCAL player currently has
  this entity as their own `ContextInteraction.ActiveTarget` every `LateUpdate` (a different,
  per-tick, per-viewer question from `PoiActivation.State`'s own shared, every-client-agrees-on
  fact) and reacts to that player's own `State`:
  - `Available`/`PhaseUnavailable`/`AlreadyUsed`/`NotNeeded` - shown, `titleText` stays constant
    (set once in `Setup`) while `descriptionText`/`descriptionRoot` are set to the matching
    authored description and hidden entirely whenever that state's description is empty.
    `AlreadyUsed` is a special case as of 2026-08-29 (`ResolveAlreadyUsedDescription`): it reads
    `PoiUsage` straight off the simulation for the targeting player, and if it finds a live
    `PoiUsageEntry.CooldownRemaining > 0` for THIS POI shows a real-time `"Xs"` countdown instead of
    the plain authored `promptAlreadyUsedDescription` text - same "View reads the sim state
    directly for a live number" precedent the Revive bleed-out timer below already sets. Only ever
    fires for a `PoiUsagePolicy.Cooldown` POI (`MarkUsed` writes `CooldownRemaining = 0` under every
    other policy), so a `OncePerPlayerPerBreak`/`PerRun` POI's `AlreadyUsed` still shows its plain
    authored text unchanged.
  - `Busy` - hidden entirely (the real Choice Window is already open at that point, a floating
    label on top of it would be redundant).
  - `None` - hidden (nobody local is targeting it).

  Separately, `InteractionPromptWidget` also subscribes to `EventContextInteractionRejected`
  (`QuantumEvent.Subscribe`, same idiom `HitFeedback` uses) - fired by
  `HealingShrineUtility.TryInteract` only on an actual rejected PRESS while `NotNeeded` (never from
  mere proximity - see "Context Interaction" above), filtered to `e.Target == _entityRef` and
  `e.Player` being one of this client's own local players, and pops a `ToastManager.Show(...)`
  toast with that same `promptNotNeededDescription` text - gated by an inspector-toggleable
  `toastOnNotNeeded` bool.

  Shown/hidden via a scale pop (`PrimeTween.Tween.Scale`, `Ease.OutBack` in / `Ease.InQuad` out,
  `useUnscaledTime: true` so it stays responsive even if some OTHER player's Level-Up screen has
  ramped `Time.timeScale` down match-wide) rather than an instant `SetActive` snap - same
  "pop in with overshoot" idiom `DamageNumberUiWidget`/`ColliderVisualScaleView` already use. The
  widget's own root GameObject stays active the whole time (only a child `visualRoot` is
  scaled/toggled) so `LateUpdate` keeps running to re-check state every tick regardless of current
  visibility.
- **Cursed Rift's own Choice Window** - reuses `GameplayUiController.choiceWindows[]`, the SAME
  per-slot `ChooseWindow` instance a real Level-Up already uses, driven by a second method
  (`UpdateCursedRiftWindow`) rather than a second window - see `docs/choice-window-refactor.md`.
- **`ToastManager`** (`Assets/_Project/Scripts/UI/Common/ToastManager.cs`, + `ToastWidget.cs`) -
  NOT new, and NOT Healing-Shrine-specific: a pre-existing generic pooled toast popup already used
  by `PartyManager`/`MainMenuWindow` ("Everyone is ready!", "Joined party", etc.) - originally lived
  under `UI/Menu/`, relocated to `UI/Common/` once `InteractionPromptWidget` became a second, in-game
  caller (a duplicate `Hud/ToastManager.cs` was written by mistake first, without checking for an
  existing one, then deleted once found - see git history if curious). Static `Instance`, a `_pool`
  of `ToastWidget` children found via `GetComponentsInChildren` at `Awake` (supports several
  simultaneous toasts, unlike a single-slot popup), `Show(string message)` claims the first free one.
  Each scene needing toasts wires its own `ToastManager` + pooled `ToastWidget` children scene
  object - the CURRENT gameplay scene (`QuantumGameScene.unity`) has none; an earlier revision
  (`Assets/gamesceneBackup.unity`) did, useful as a reference for rebuilding it. Not
  split-screen-scoped (shows identically for both local players) - same limitation
  `WindowManager`-style singletons in this codebase already have.

## Currency (Coin/Rift Shard sacrifices) - see `docs/choice-window-refactor.md`'s companion note

Coin/Rift Shard Offering sacrifices spend from the interacting player's OWN wallet
(`CharacterStats.Coins`/`RiftShards`) - both currencies moved from shared `Frame.Global` totals to
per-player wallets as part of this same pass, confirmed with the user (a pickup now credits every
connected player the same base amount, each scaled by their own gain multiplier). See
`docs/global-upgrades.md`'s "Economy" section for the currency system itself.

## Editor authoring needed

1. **`SurvivalConfig.Phases[]`'s `IsBreathing` entries** (manual Inspector step - see
   `docs/run-phase.md`), plus `CursedRiftConfig`/`SacrificePoolData` + the 3 `SacrificeDefinition`
   instances, authored by `Tools/RiftRaiders/Generate Breathing POI Content`
   (`BreathingPoiContentGenerator.cs`). Still needs `RuntimeConfig.CursedRiftConfig` assigned by
   hand (`QuantumMenuConfig.asset`).
2. **`HealingShrine`/`CursedRift` `EntityPrototype`s** - hand-placed directly in a level chunk,
   same pattern `Chest` already uses. Both now need an `Interactable` component (a real `Radius`, a
   `Priority`, and `Kind` set to the matching `InteractableKind` value) - `HealingShrine.prefab`
   already has one authored (Radius/UsagePolicy already match its own `HealingShrine` component),
   but `Kind` was authored back when `CursedRift` was the only enum value (`= 0`) and now needs
   flipping to `HealingShrine` by hand in the Inspector.
3. **`choiceWindows[0]`** (`GameplayUiController`, the existing Level-Up instance, reused directly
   - see `docs/choice-window-refactor.md`) still needs `subtitleText` (`TMP_Text`) built, plus
   `valuePreviewText`/`buttonLabelText` on its own card template - none exist in the scene yet.
   `secondaryButton` (formerly `keepCurrentButton`) needs no new Editor work - Cursed Rift's own
   "CANCEL" reuses the same already-wired button Choose-Weapon's "KEEP CURRENT" already uses. No
   second window/Canvas needed anymore.
4. **`SkillCooldownUiWidget.contextInteractionIcon`/`interactPromptRoot`** - unassigned, needs a
   real interaction icon sprite + prompt UI on the HeroSkill-slot HUD instance.
5. **`PoiView`'s Inactive/Active/Expired child visuals** (already wired on `HealingShrine.prefab`;
   `PoiView` is also referenced by the in-progress `CursedShrine.prefab`, which still needs its
   `QPrototypeHealingShrine` swapped to the real `CursedRift`/`Interactable` components) and the
   HUD-side **`InteractionPromptWidgetManager`** scene setup (`widgetPrefab`/`widgetParent`
   assigned, an `InteractionPromptWidget` prefab built under the HUD Canvas) - neither exists yet.
6. **Real `Icon` sprites for the 3 `SacrificeDefinition` assets** - left unassigned by the
   generator.
7. **`ToastManager`** - no scene instance in the current `QuantumGameScene.unity` (a `ToastManager`
   + pooled `ToastWidget` children under a Canvas, matching `Assets/gamesceneBackup.unity`'s own old
   setup, which can be copied/reused as a reference) - until one exists, `ToastManager.Instance`
   stays null and the `NotNeeded` toast silently no-ops (`?.Show(...)`), it just won't show
   anything, nothing breaks.
8. **Manual end-to-end test not yet run**: solo pass (Breathing triggers, Shrine heals, Rift
   redirects the Base Skill button and opens the Sacrifice screen, Cancel/Confirm/mutation-pick all
   work, Rift doesn't re-trigger until the next Break) and a couch co-op pass (P1's window doesn't
   affect P2's screen/movement, P2 can independently use the same Rift, a Break ending
   mid-sacrifice-selection closes cleanly while mid-mutation-selection leaves the window open,
   Coin/Rift Shard HUD numbers are genuinely independent per player after a shared pickup).
