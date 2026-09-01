# Recoverable Accessory Guard

Every hero wears a **Signature Accessory** that eats one incoming hit outright, loses a durability
point, and physically pops off into the world as a collectible the owner has to walk back for.
Durability persists across the whole run and is only ever restored by paying a Merchant.

The mechanic deliberately does two different jobs in the two halves of the run loop:

- **during Survival** it's a *spatial* resource - the guard is gone until you physically go and get
  it back, which means a block relocates you, mid-fight;
- **during Break** it's an *economic* one - restoring it competes with weapons, perks and food for
  the same Coins, and the price rises the more damaged it is.

```
Survival at 3/3 -> block -> 2/3, accessory pops away -> recover it -> keep fighting
                                                     -> reach Break at 2/3
                                                     -> repair for 25, or keep the Coins
```

**The gameplay system is completely hero-agnostic.** Nothing in the simulation - and nothing in the
View that drives behaviour - ever asks *which* hero this is, or *what* the accessory actually is.
Per-hero presentation is one struct on `CharacterData`'s View partial, read only to pick a sprite
and a prop.

---

## File map

Simulation:

| File | Role |
| --- | --- |
| `Simulation/QTN/Accessory/AccessoryGuard.qtn` | `AccessoryGuardState` enum, per-player `AccessoryGuard`, per-collectible `DroppedAccessory` |
| `Simulation/Assets/Config/AccessoryGuardConfig.cs` | the one tuning asset - durability, pop/pickup, repair prices. Also `AccessoryServiceKind` |
| `Simulation/Systems/Accessory/AccessoryGuardUtility.cs` | seed / block / spawn collectible / recover / restore |
| `Simulation/Systems/Accessory/AccessoryGuardSystem.cs` | Airborne→Dropped landing, owner-only pickup, orphan cleanup |
| `Simulation/Systems/Accessory/AccessoryServiceUtility.cs` | the Merchant half - which service, what price, buy it |
| `Simulation/Commands/BuyAccessoryServiceCommand.cs` | zero-payload purchase command |
| `Simulation/Assets/Character/CharacterData.View.cs` | **new** - `HeroAccessoryPresentation` (the per-hero half) |

Edited: `QTN/Events.qtn` (5 new events), `Default/RuntimeConfig.User.cs` (`AccessoryGuardConfig` +
`Prefabs.DroppedAccessoryPrototype`), `Default/SystemSetup.User.cs`, `Default/CommandSetup.User.cs`,
`Systems/Combat/DamageUtility.cs` (the block hook), `Systems/Player/CharacterSystem.cs` (seeding),
`Systems/Poi/StoreSystem.cs` (the new command case).

View:

| File | Role |
| --- | --- |
| `View/Entities/Player/AccessoryView.cs` | the EQUIPPED visual - switches the hero's worn/unworn GameObjects off `AccessoryGuard.State` |
| `View/Entities/Accessory/DroppedAccessoryView.cs` | the WORLD COLLECTIBLE - paints the shared prototype with the owner's sprite, and spins it in the air |
| `View/Entities/Player/MovementRingView.cs` | *edited* - the RADAR: a fourth ground arrow on the local player's own ring, aimed at their dropped accessory |

Edited: `_Project/Scripts/UI/InGame/GameplayUiController.cs` (the Merchant service card).

Editor: `Editor/AccessoryGuardContentGenerator.cs` (`Tools > RiftRaiders > Generate Accessory Guard
Content`).

---

## State machine

```
        Equipped  --block a hit (durability > 1)-->  Airborne  --arc lands-->  Dropped
           ^                                                                     |
           |                                     owner walks into pickup radius  |
           +---------------------------------------------------------------------+
           |
           |  Merchant repair / replacement (always straight back to full)
           |
        Broken  <--block a hit that takes durability to 0 (flies as DEBRIS, never collectible)
```

`AccessoryGuardState.Equipped` is enum value 0, so a freshly added component reads as "worn" with no
explicit seeding - the same zero-default convention `PlayerLifeStateKind.Alive`/`GameState.Lobby`
already use.

**Airborne vs Dropped** are two halves of one collectible entity's life, not two entities. The
collectible is visible in both; it is only *pickable* once it has actually landed. That transition is
read straight off `PopVelocity`'s own presence - `PopMotionSystem` removes it the instant the arc
lands (`PopVelocity.qtn`) - rather than a second timer. So an accessory can't be re-caught in mid-air
on the same tick it popped off.

**Broken still flies the accessory, as debris.** The hit looked identical from the player's side, so
the accessory is visibly knocked off and arcs away exactly like a recoverable drop - it is just
flagged `DroppedAccessory.Broken`, which makes it non-collectible, untracked by the owner's guard
(`Accessory` stays `None` - it's fire-and-forget), and destroyed by `AccessoryGuardSystem` the moment
it lands. `AccessoryBroken` fires **at that landing point**, not at the player, so the shatter VFX
(`EffectsManager.accessoryBrokenEffectPrefab`) plays where the debris actually came to rest. The
owner's `State` is `Broken` from the instant of the hit regardless, so the worn visual vanishes on
impact while the debris is still in the air. There is nothing to walk back for; the only way out is a
Merchant replacement.

---

## Blocking a hit

The hook is a single early-return inside `DamageUtility.ApplyDamage`:

```csharp
if (bypassOutgoingResolution == false && AccessoryGuardUtility.TryBlock(f, target, owner, damage) == true)
    return;
```

Placement is load-bearing in three ways:

1. **Above every resolution step.** A block *negates* the hit rather than mitigating it, so it must
   roll no crit, apply no elemental proc, build no Rage/Resonance, and fire no `OnWeaponHitLanded`/
   `OnHealthDamageApplied` signal. Returning before any of it is the only way to guarantee all of it
   at once. (This also means a blocked hit can never interrupt a teammate's revive channel - see
   `ReviveDamageInterruptSystem`, which is driven by exactly those signals.)
2. **Below the `Invulnerable` check.** A player already protected by Cheat Death or post-revive grace
   must not silently burn a durability point on a hit that was never going to land.
3. **Gated to `bypassOutgoingResolution == false`.** This is the same gate `OnWeaponHitLanded`
   already uses to exclude DoT-tick replays, and it happens to exclude the other two already-resolved
   sources for free: `PlayerFallSystem`'s fall damage (an accessory shouldn't cushion a fall - and it
   would drop into the pit the player just fell into) and `SentryDecaySystem`'s self-drain. No
   special-casing needed for any of them.

**Multi-hit sources are self-limiting, with no cooldown or i-frame window.** `TryBlock` only ever
fires while `Equipped`; the first pellet of a shotgun blast pops the accessory off, and every later
pellet in that same tick finds `State != Equipped` and lands normally.

---

## The dropped collectible

One shared, fully generic `EntityPrototype`
(`RuntimeConfig.Prefabs.DroppedAccessoryPrototype`) - **not one per hero**. Every gameplay behaviour
of the pickup is identical for all heroes; only the sprite differs, and a sprite is presentation, so
`DroppedAccessoryView` swaps it at spawn by resolving `DroppedAccessory.Owner`.

It reuses `OrbSpawnUtility.SpawnWithPop` for the arc and landing, with an extra random burst on top
so a block reads as the accessory being *knocked* off rather than placed.

Three deliberate differences from a currency orb:

- **It may land on higher ground.** `PopMotionSystem` normally refuses to let a drop settle more than
  0.5 above the floor it popped from - a coin you have to go and climb for is a chore, not a reward.
  For the accessory that is exactly backwards: retrieval *is* the mechanic, so an awkward landing is
  a feature. Opted out per drop via `PopVelocity.CanLandHigher` (false for every currency/scrap
  caller, so nothing else changes), driven by `AccessoryGuardConfig.CanLandOnHigherGround`.
  Deliberately a per-drop opt-out rather than a tunable height: the clamp is a reachability guard,
  not a balance knob.

- **Collection returns it to its OWNER, whoever picks it up.** With
  `AccessoryGuardConfig.AllowAllyRecovery` (default on) any player can recover a dropped accessory
  and it goes straight back to its owner - see "Co-op recovery" below. Untick it for owner-only.
  A Downed/KO player can't collect either way (`PlayerLifeStateUtility.IsIncapacitated`), so nobody
  vacuums one up while collapsed on top of it.
- **No lifetime.** There is deliberately no `DestroyAfterTime`/`OrbLifetime` equivalent. A timer would
  silently turn a *recoverable* resource into a broken one. It waits indefinitely; it's only destroyed
  on pickup, on a Merchant restore, or when orphaned (owner gone).

### Spin while airborne

`DroppedAccessoryView` spins the sprite around its local Y while the accessory is in the air, then
eases it to **Y = 0** as it lands. Airborne-ness is read straight off `PopVelocity`'s presence - the
exact same signal `AccessoryGuardSystem` uses to flip `Airborne -> Dropped` - so the visual can never
disagree with the simulation about whether it has landed.

The settle always finishes by continuing **forward** to 0 in the spin's own direction rather than
winding back, so it never visibly reverses; `landingSettleDuration` 0 snaps instead.

> **Bug worth remembering:** the first implementation accumulated the angle by reading
> `spinTransform.localEulerAngles.y` back each frame and adding to it. That silently does nothing on
> a tilted transform - Unity derives those Euler angles from the underlying quaternion, and with a
> non-zero X (`AccessoryOrb`'s sprite child sits at X = 45) the decomposition returns a
> different-but-equivalent triple once the spin passes 90°, so the value read back is not the value
> written and the accumulation stalls. The angle is now owned in a plain `float` field and the
> rotation is rewritten wholesale every frame.

**Billboarding and the spin are applied together, in one write.** `Billboard` sets an absolute world
rotation every `LateUpdate`, so a local spin on that same transform is simply erased - and leaving
both enabled makes the result depend on script execution order, i.e. it would work or not work
arbitrarily. `DroppedAccessoryView` therefore reproduces `Billboard` verbatim (same
`LookRotation(camera.forward, Vector3.up)`) in its own `LateUpdate` and composes the spin on top **in
the billboard's own space**, and disables any `Billboard` component it finds on the spin transform so
there is exactly one writer. `billboardToCamera` off restores the fixed-angle-prop path, where only
the spin axis is driven and the authored local rotation is preserved.

`spinAxis` defaults to **Z** - in billboard space that's the screen plane, so the accessory rolls
face-on to the camera and stays fully visible the whole way round. Y yaws it instead, which takes a
flat sprite edge-on twice per turn.

One authoring note: **the spin turns around the transform's pivot**, so centre the sprite's pivot (or
park a dedicated `spinTransform` at the sprite's centre), or it will swing in an arc rather than spin
in place.

### Block feedback (flash + hit stop)

A blocked hit returns from `ApplyDamage` before `EntityDamaged` is ever fired - that's what makes it
a true negation - but it also means every existing damage reaction is bypassed, and without something
in its place a block reads as the enemy having *missed*. Two listeners hang off `AccessoryBlocked` to
put the impact back:

- **`HitFeedback`** flashes the character in its own `blockFlashColor` (default white, deliberately
  distinct from the damage colours since nothing was actually lost). Routed through `FlashDamage`,
  the top-priority tier, because a block *is* an impact and shouldn't lose to a heal/shield/pickup
  glow landing the same moment.
- **`HurtOverlayUiWidget`** triggers the screen flash and a hit stop of `blockHitStopDuration`
  (0.12s default) for the local player. A flat authored value rather than the damage%-keyed
  `hitStopTiers` a real hit uses, because there is no damage to scale off. It defers to the dying
  blink exactly like a real hit does.

### Max's Overdrive head sprites

Max is the one hero whose head sprite is already swapped by something else - `BerserkFxView` drives
three tiers (Normal / Berserk / Overdrive). With the guard in play he can be in any of those tiers
either wearing the cap or not, and `AccessoryView` swaps a whole ROOT between those two cases - so
the `headSprite` renderer `BerserkFxView` points at lives inside only one of them. Going Overdrive
while hatless wrote the tier sprite onto a hidden renderer, and nothing changed on screen.

`BerserkFxView` therefore carries a parallel hatless set - `noAccessoryHeadSprite` (the renderer in
the other root) plus `berserkNoAccessoryHeadSprite`/`overdriveNoAccessoryHeadSprite`, each falling
back to its with-hat counterpart when unassigned - and writes **both** heads on every tier change.

It deliberately does **not** read `AccessoryGuard`. Keeping both roots' heads correct for the current
tier, and letting `AccessoryView` independently decide which root is visible, means no accessory
logic is duplicated, the two components have no ordering dependency, and a hat knocked off
mid-Overdrive needs no reaction at all - the other root was already showing the right sprite. Leaving
`noAccessoryHeadSprite` unassigned reproduces the previous behaviour exactly, so no other hero is
affected.

### Co-op recovery

Any player can walk over a dropped accessory and it returns **instantly to its owner** - it never
goes to whoever picked it up. That single rule is what keeps co-op cheap: there is no
carried-accessory state, no carrier-death or carrier-downed handling, no delivery proximity check,
and no second visual. `AccessoryRecovered` carries a `Recoverer` alongside `Owner` (equal when they
fetched it themselves) purely so the View can credit the right person.

**Why automatic rather than a deliberate press:** the spatial cost the mechanic is built on is
*travel*, and that cost is paid in full either way - somebody had to physically go to the spot. Making
a teammate press a button on arrival adds ceremony without adding a decision, and the accessory lands
1-3 units from where the owner was hit, so anyone near enough to reach it is already in that fight.
Automatic just means the team doesn't lose a defensive resource when the owner can't safely go back
for it, which is exactly what co-op assistance should do.

**No proximity requirement, deliberately.** A teammate returns it from wherever they found it. Two
options were considered and rejected:

*Requiring the owner to be nearby* was built and then reverted. The scenario barely occurs (the
accessory lands 1-3 units from where the owner was hit, so a teammate standing on it while the owner
is far away means the owner ran a long way after dropping it), and the gate's failure mode is worse
than either extreme: a teammate walks over the hat and *nothing happens*, with no feedback explaining
why. Doing it properly would need a prompt for that case. And what it protects against isn't a
problem - a teammate returning the guard from across the arena exploits nothing, since the owner
already paid the real penalty of being without it the whole time it was down.

*Full carry-and-deliver* was rejected for a different reason: the helper already paid the travel cost
getting to it, so making them walk it back charges the *helper* twice while the owner pays nothing,
inverting who the mechanic is meant to pressure. It also needs a genuine carry state - a visual on the
carrier, a rule for dropping it when they go Downed, a delivery radius.

The two coherent designs are the extremes, and both are reachable from data: `AllowAllyRecovery` off
for owner-only (losing it has real teeth), or on for ally-from-anywhere (the team can bail each other
out). The middle mostly buys ambiguity.

If accidental returns ever feel too cheap in playtest, the deliberate-press variant is a small,
well-trodden change rather than a redesign: give the collectible an `Interactable` + a new
`InteractableKind`, and dispatch from `SkillSystem` exactly as Cursed Rift/Shrine/Store/Blacksmith/
Revive already do. The pickup path itself would not move.

### Recovery feedback

Putting the accessory back on - by walking over it, or by paying the Merchant - plays a particle at
the pickup point (`EffectsManager.accessoryRecoveredEffectPrefab`, positioned from
`AccessoryRecovered.Position`, i.e. where the accessory was lying, not where the player is) plus a
`HitFeedback` character flash in `recoverFlashColor`. That flash is deliberately routed through the
LOW-priority tier - the same one a currency pickup uses - so getting your guard back can never stomp
a hit flash landing in the same moment. `AccessoryRestored` (the Merchant path) flashes the same way.

### HUD readout

`CharacterUiWidget` gained an `accessoryEquippedRoot` (shown only while `State == Equipped`) and an
`accessoryGuardPips[]` array - one object per durability point, deactivated from the right, so index
`i` is shown while `i < CurrentDurability`. Both are plain `SetActive` swaps off `AccessoryGuard`,
the same idiom every other indicator on that widget uses, and both hide entirely for an entity with
no guard - so the one shared prefab still serves every enemy. Authoring fewer pips than
`MaxDurability` logs a one-shot warning rather than silently under-reporting forever.

A third field, `accessoryGuardRoot`, hides the pip STRIP itself (its empty frames and backing
image, the part no pip owns) for any entity with no `AccessoryGuard` - the pips going dark
individually still left a row of empty guard slots hanging on every enemy and every Lux sentry,
which read as a broken readout rather than as an absent mechanic.

Recovery restores the **state, not the durability** - recovering a 2/3 accessory gives back a 2/3
accessory. That's what makes the Merchant decision matter at all.

### Break notice (the toast)

A break is the one accessory transition with **nothing left in the world to explain itself**. A
normal block pops the accessory off as a collectible with a radar arrow aimed straight at it, so
what happened and what to do are both visible. At 0 durability no collectible spawns at all - only
debris that destroys itself on landing - so the accessory just stops existing, and the only way back
(paying the Merchant at the Store, see `docs/store-blacksmith.md`) is communicated nowhere.

So `EffectsManager.OnAccessoryBroken` - which already handles this event for the shatter VFX - also
pops a `ToastManager` message, "ACCESSORY DESTROYED / Buy a new one at the Store". Deliberately no
component of its own: it is one line on an existing subscriber, and the persistent "you have none
left" state is already `CharacterUiWidget`'s pips going dark.

It is filtered to this client's own local player(s) (`MyLocalPlayer.IsLocalEntity`, the same
membership check `HurtOverlayUiWidget` uses for the block flash, so couch co-op's second local player
pops it too), unlike the shatter VFX right below it, which plays for everyone - a teammate's break is
theirs to act on. It is also raised BEFORE the effect-prefab lookup, which early-returns when no
shatter particle is authored.

### Radar (finding it again)

A dropped accessory has **no lifetime** and is only ever recovered by physically walking back to it -
that retrieval *is* the mechanic. But a block can knock it well off-screen (it arcs, and unlike every
currency drop it may land on higher ground), so without a pointer the *spatial resource* half of the
design degrades into sweeping the level for a small sprite.

That pointer is a **fourth ground arrow on the local player's own movement ring**
(`MovementRingView`), not a HUD element: the same flat world sprite the move/trail/target arrows
already are, orbiting the character at an authored offset and rotated toward the accessory. It is
tinted from the hero's own `RingColor` like the ring and move arrows - unlike the target arrow, which
keeps its authored colour - so the pointer reads as part of that player's own ring. Only its authored
ALPHA survives the tint, and both it and the icon ride the ring's grounded fade. The ICON is not
tinted: it is the accessory's own artwork, not an indicator.

Because that fade rewrites `sprite.color` every frame, the colour these two show comes from the
component, not from the `SpriteRenderer` - editing that mid-play is overwritten on the next frame. It sits directly where the player is already looking, needs no screen real estate, and
shares the ring's own grounded fade so it disappears with everything else when they leave the ground.

**It lives in `MovementRingView` rather than a component of its own** precisely because it is the
same job as the existing target arrow - a flat ground arrow orbiting the character, aimed at a world
entity. A separate near-identical component would be one more thing to keep in sync by hand, the
exact drift this codebase has already been bitten by more than once.

- **Optional, off by default.** The whole feature is gated on `accessoryArrowSprite` being assigned,
  and every access to it (including inside the shared `PositionAndRotateArrow`) is null-tolerant, so
  a hero prefab authored before this existed keeps working untouched.
- **An optional companion icon** (`accessoryIconSprite`) rides the same heading a little further out,
  painted once at `Initialize` from that hero's own `CharacterData.Accessory.CollectibleSprite` - the
  same sprite `DroppedAccessoryView` puts on the pickup, so the pointer and the thing it points at can
  never show different art. It **never takes the arrow's rotation** while the arrow turns: it's a
  picture of the accessory, not a direction indicator, and a rotated hat reads as broken art. Instead
  it **billboards to the camera** (`accessoryIconBillboard`, on) so it stands up and reads like the
  world pickup rather than lying flat like the three arrows around it - applied here, in world space,
  with any `Billboard` component on that sprite disabled automatically, the same one-writer-per-rotation
  rule `DroppedAccessoryView` documents for the pickup's own spin. Turned off, it keeps the flat
  authored rotation instead.
- **Both sprites pop in on appear** - a scale tween from 0 to their authored scale, `Ease.OutBack` over
  `accessoryAppearDuration` (0.3s), edge-triggered on the transition INTO shown (the block that dropped
  it, or walking back out past `accessoryHideWithinDistance`), never per frame. Scale rather than
  alpha, because alpha is already spoken for by the grounded fade multiplying into it every frame -
  and the overshoot is what makes a pointer register in peripheral vision. Both tweens are stopped in
  `DeInitialize`, the same cleanup the ring's glow pulse already does. `0` duration skips it.
- **And shrink away on the mirror transition** - `Ease.InBack` to 0 over `accessoryDisappearDuration`
  (0.2s), whether the accessory was recovered or you simply walked inside the hide distance. This one
  needs a render window: `SpriteRenderer.enabled` follows a separate `_accessoryRendering` flag rather
  than "is there something to point at", because otherwise the renderers would switch off on the same
  frame the accessory stopped being tracked and the animation would never be seen. The flag clears on
  the tween's own completion - and `Stop()` never fires that callback, so an appear interrupting a
  shrink can't turn the renderers off behind its own back. The shrink starts from the sprite's CURRENT
  scale, so interrupting a half-finished pop-in doesn't jump. For the
  same reason it is drawn **untinted** - its RGB is forced to white at capture rather than taken from
  the prefab, so a colour authored on that renderer (or inherited from duplicating the `TargetArrow`
  child to make it) can't recolour the artwork; only its authored ALPHA is kept, so it still rides the
  ring's grounded fade.
  Unauthored heroes keep whatever the prefab baked in rather than blanking out, the same
  `keepPlaceholderWhenUnauthored` default the pickup itself uses.
- **The icon is normalised to a fixed world size** - `1x1` world units multiplied by
  `accessoryIconScale`, with the sprite's own pixel size and PPU divided out (the same job
  `ChunkDetailScatter.ResolveUnitScale` does for hand-placed detail props). One hero's accessory art
  has no idea how big another's is, so without this the pointer would change size per hero for no
  design reason. It REPLACES the prefab's scale on that sprite rather than multiplying it; `0` opts out
  and keeps the prefab's own.
- **And centred on the orbit point, not pivoted on it** - sprite pivots are rarely dead centre (a
  bottom-pivoted cap would hang half its height off the point), so the icon is shifted by its own
  pivot->centre offset, applied after the billboard write in that rotation's own right/up axes so it
  stays correct however the sprite faces. The offset is stored unscaled and multiplied by the icon's
  LIVE scale, so it stays right mid-pop-in instead of over-shifting a half-grown sprite. It moves the
  icon within its billboard plane only, so the authored local Y still decides how high off the ground
  the icon's centre rides.
- **It reads `AccessoryGuard.Accessory` directly**, the same tracked entity the simulation owns, so it
  cannot disagree with the pickup about where it is. Broken debris is deliberately untracked by the
  guard (`Accessory` stays `None`), so "is there something to walk back to" needs no extra state - a
  break correctly shows nothing.
- Position comes from `EntityViewManager.GetEntityTransform` (the interpolated view, same lookup the
  target arrow uses), falling back to the simulation's `Transform3D` - which is what actually runs
  unless the dropped-accessory prototype is given an `EntityViewCacheInit`.
- Direction and distance are **XZ only**: an accessory that landed on a ledge above shouldn't read as
  further away than one at your feet.
- `accessoryShowWhileAirborne` (on) points at it during the pop arc too - that is exactly when you
  most want to see where it is heading - and `accessoryHideWithinDistance` (6) drops the arrow once
  it's close enough that the pickup speaks for itself.

Local-player-only comes for free: `MovementRingView` already sets `executeOnlyOnLocal` and activates
its sprites only for the local player. Ally recovery is allowed (`AllowAllyRecovery`), but a
teammate's drop is their own errand and their own ring's arrow.

---

## Merchant repair & replacement

Sold on the **Store**'s own screen (the Merchant POI - see `docs/store-blacksmith.md`), modelled
directly on Store's existing guaranteed *"Increase Weapon Level"* offer: not part of the shared rolled
`StoreInventory`, price resolved live off the buyer's own state, bought with its own dedicated
zero-payload command.

| Durability | Service | Prototype cost |
| --- | --- | --- |
| 3/3 | *(none - no card is shown at all)* | - |
| 2/3 | Repair → 3/3 | 25 |
| 1/3 | Repair → 3/3 | 50 |
| 0/3 Broken | **Replace** → 3/3 | 100 |

Costs live in `AccessoryGuardConfig`:

```csharp
public FP[] RepairCostByMissingDurability = { 25, 50 };  // index 0 = 1 missing, index 1 = 2 missing
public FP  BrokenReplacementCost = 100;
```

An array indexed by **missing durability** rather than three named fields, so raising
`BaseDurability` past 3 later needs one more array entry instead of a new field and a new branch.
Past the authored range the last entry holds, the same convention `StoreConfig.BreakWeaponConfig`/
`SurvivalConfig.Phases[]` already use. These are explicit per-step costs, **not** a formula - no
dynamic pricing this pass.

The one invariant authoring can get wrong ("more damaged → more expensive", "replacement > any
repair") is checked by an Editor-only `OnValidate` on the config, so a designer finds out while
typing the number rather than three Breaks into a playtest.

### Design rules this upholds

- **Repair always restores directly to `MaxDurability`.** There is no partial restore anywhere in the
  feature - no "buy +1 point" path exists to accidentally take. One click, one clear decision.
- **Declining is free and sticky.** Nothing resets durability between phases. `AccessoryGuard` is
  written by exactly four places (seed, block, recover, restore) and none of them is phase-driven, so
  a player who walks away at 1/3 starts the next Survival at 1/3.
- **It never consumes the weapon purchase allowance.** `StoreUtility.ResolveWeaponOfferCount` and
  `StorePurchases.Entries` are only ever consulted for *rolled* offers; this touches neither. It also
  needs no once-per-Break tracking of its own: a successful service restores to full, which
  immediately resolves the player to `AccessoryServiceKind.None`, so a second purchase this Break is
  impossible without first losing durability again. **The state is the limit.**
- **Insufficient Coins disables, doesn't hide.** `PurchasableCardState.CanAfford` leaves the card
  visible but its Buy button non-interactable, the same affordance every other Store card uses.
- **Per-player and deterministic.** Everything is per-player components + a per-player command, so
  Max at 1/3, Zara at 3/3 and Brute Broken each see their own service (Repair / nothing / Replace) at
  the same Merchant in the same Break, with no shared state between them.

### Repairing while the accessory is still lying in the level

Deliberately allowed. Service eligibility is derived purely from durability, never from world state -
the shop doesn't refuse to help until you've walked back for it. `AccessoryGuardUtility.Restore`
reconciles it by destroying the outstanding collectible, which is what upholds the
never-both-worn-and-on-the-floor invariant below.

---

## View separation

**No simulation system ever touches a GameObject.** The simulation only ever changes
`AccessoryGuard.State`/`CurrentDurability`/`Accessory` and `DroppedAccessory.Owner`; the View observes
that and updates presentation.

```
Quantum simulation                     Unity view
------------------                     ----------
AccessoryGuard.State = Equipped   ->   AccessoryView   -> prop.SetActive(true)
AccessoryGuard.State = Airborne   ->   AccessoryView   -> prop.SetActive(false)
                                       DroppedAccessoryView (collectible exists, arcing)
AccessoryGuard.State = Dropped    ->   AccessoryView   -> prop.SetActive(false)
                                       DroppedAccessoryView (collectible exists, landed)
AccessoryGuard.State = Broken     ->   AccessoryView   -> prop.SetActive(false)
                                       (no collectible exists at all)
```

### Why polling, not the events

`AccessoryView` polls `AccessoryGuard.State` every `QUpdate` rather than subscribing to
`AccessoryBlocked`/`Recovered`/`Broken`/`Restored`. State is authoritative and self-healing, so a
late-joining view, a rollback, a resimulated tick or a missed event can never leave a hero visibly
wearing something the simulation says they dropped. This is the same reasoning
`BlobAnimationView`/`WeaponViewController` already document for their own Downed/KO swaps, and this
codebase's established convention for *continuous* state.

The five events still exist, for the job events are actually good at: **one-shot moments** - an
impact spark on a block, a landing puff, a pickup sound, a repair flourish. Nothing subscribes to
them yet (same "vocabulary now, wired later" precedent `PlayerDowned`/`PlayerKO` already set).

### No duplicate visuals

The invariant "*Max is visibly wearing his cap AND his cap is on the floor*" is structurally
impossible, because both sides key off the same authoritative state:

- `AccessoryGuard.Accessory` is one-to-one - the guard can only pop off while `Equipped`, so a player
  can never have two outstanding collectibles.
- The prop is shown **only** while `State == Equipped`, which is exactly the state in which no
  collectible exists.
- Every path back to `Equipped` (pickup, repair, replacement) funnels through code that destroys the
  collectible in the same call.
- `AccessoryGuardSystem` additionally destroys any collectible whose owner's guard is no longer
  tracking it, so a stale entity can never linger and become re-pickable.

### Per-hero presentation

One struct, on `CharacterData`'s View partial - the only place in the feature that knows an accessory
is a specific thing:

Per-hero presentation lives in **two** places, split by what kind of thing it is.

**Per-hero assets** go on `CharacterData`'s View partial:

```csharp
[Serializable] public struct HeroAccessoryPresentation
{
    public string DisplayName;        // "Lucky Cap" - Merchant card text only
    public Sprite CollectibleSprite;  // the WORLD PICKUP -> DroppedAccessoryView
    public float  CollectibleScale;   // per-hero size correction on the SHARED prototype
}
```

`CollectibleScale` exists precisely *because* the collectible prototype is shared: a cap, a headset
and a mask are rarely drawn at the same source size, so without it that one prefab's scale would have
to suit every hero at once. It multiplies the prototype's authored scale rather than replacing it,
and 0/unset reads as 1 - the same "an unset multiplier defaults safely" convention
`EnemyFactionSkin.ScaleMultiplier` already uses. The EQUIPPED visual needs no equivalent: that one is
a hand-placed GameObject on the hero's own view prefab, so it's scaled directly in the Editor.

This follows the project's existing simulation/view split convention exactly - a `.View.cs` partial
on a simulation `AssetObject`, the same shape `PassiveData.View.cs`, `EnemyDataAsset.View.cs`,
`SkillData.View.cs` and `WeaponDataAsset.View.cs` already use (and `CharacterData.cs` itself already
carries `Sprite PawnSprite`/`Color RingColor`, so Unity references on this asset are not new).

**Per-hero rig references** go on that hero's own view prefab, as two GameObjects `AccessoryView`
switches between:

| `AccessoryGuard.State` | `equippedVisual` | `unequippedVisual` |
| --- | --- | --- |
| `Equipped` | ON | OFF |
| `Airborne` / `Dropped` / `Broken` | OFF | ON |

The worn accessory is deliberately **not** a prefab instantiated from hero data. These rigs are
sprite-based (`head_0`, `Torso_0`, `CharBody`), so "wearing the cap" and "not wearing the cap" are
normally two different authored head sprites, not one prop parented onto a bare head - a swap
expresses that directly, an instantiated prop doesn't. It's also the exact active-object-swap idiom
`BlobAnimationView` already uses for Alive/Downed/KO and `PoiView` for Inactive/Active/Expired, and
it puts per-hero rig references on the hero's prefab (where `BlobAnimationView`'s already are) rather
than in an asset.

Both fields are optional and independent: assigning only `equippedVisual` degrades to a plain single
toggle; assigning only `unequippedVisual` suits a rig whose default already includes the accessory.

**There is no `if (hero == Max)` anywhere**, in either layer - each hero's prefab simply carries its
own `AccessoryView` pointing at its own two GameObjects.

---

## Seeding

`CharacterSystem.SeedAccessoryGuard` adds the component from
`ISignalOnEntityPrototypeMaterialized`, gated on `PlayerLink` (the "is this a player character, not
some other `CharacterStats` carrier" test - only the 7 hero prototypes carry both).

Added in code rather than authored on each hero's prototype so:

- the whole mechanic switches on/off with a single `RuntimeConfig.AccessoryGuardConfig` assignment
  (unassigned, or `BaseDurability == 0`, seeds nothing and therefore blocks nothing);
- no hero prototype can silently be missing it.

Seeded from `CharacterSystem` rather than `PlayerSpawnUtility.Spawn` so a hero placed directly in a
scene for testing - which never runs through `Spawn` at all - is seeded too.

`MaxDurability` is copied onto the component at seed time rather than re-read from the config on every
check, so a mid-run config swap can't leave a player at 4/3.

---

## Acceptance criteria coverage

| # | Criterion | Where |
| --- | --- | --- |
| 1 | 3/3 shows no repair service | `ResolveService` → `None`; `BuildAccessoryServiceCardData` returns an empty card |
| 2 | 2/3 offers Repair to full | `ResolveService` → `Repair`, `ResolveRepairCost(1)` = 25 |
| 3 | 1/3 offers Repair to full at higher cost | `ResolveRepairCost(2)` = 50 |
| 4 | Broken offers Replacement | `CurrentDurability == 0` → `Replacement` |
| 5 | Replacement > repair | `BrokenReplacementCost` 100, enforced by `OnValidate` |
| 6 | Repair always restores to `MaxDurability` | `AccessoryGuardUtility.Restore` - no partial path exists |
| 7 | May decline and keep current durability | nothing auto-restores; declining is a no-op |
| 8 | Durability persists across Break/Survival | `AccessoryGuard` is written by 4 non-phase-driven places only |
| 9 | Doesn't consume weapon purchase allowance | own command; touches neither `StoreInventory` nor `StorePurchases` |
| 10 | Uses existing currency/payment | `CoinUtility.TrySpend` (per-player `CharacterStats.Coins`) |
| 11 | Insufficient Coins disables the service | `PurchasableCardState.CanAfford` → `PurchasableCardUi.Apply` |
| 12 | Independent per player in multiplayer | per-player components + per-player command |
| 13 | Each hero defines its collectible sprite | `HeroAccessoryPresentation.CollectibleSprite` |
| 14 | Collectible shows the correct owner's sprite | `DroppedAccessoryView` resolves via `DroppedAccessory.Owner` |
| 15 | Each hero has an Accessory View | `AccessoryView` on the hero's own view prefab, with its own two GameObjects |
| 16-19 | Equipped shows / Airborne, Dropped, Broken hide | one `State == Equipped` test drives both halves of `AccessoryView.Apply` |
| 20 | Pickup re-enables the correct owner's view | `Recover` → `Equipped` → next `QUpdate` |
| 21 | Merchant service re-enables the view | `Restore` → `Equipped` → next `QUpdate` |
| 22 | No hero-specific gameplay branches | no simulation file names a hero; presentation is data |
| 23 | GameObject activation only in the View layer | `AccessoryView`/`DroppedAccessoryView` are the only `SetActive`/`Instantiate` callers |
| 24 | Deterministic and Quantum-safe | components + `f.RNG`-free logic; the only randomness is `OrbSpawnUtility`'s existing deterministic pop |

---

## Known simplifications

- **The Merchant card sits in the Store's food/utility row**, packed after the rolled food offers.
  With the stock config that row is `[food, food, accessory service]` - exactly the 3 slots
  `ChooseWindow.cardCount` ships with. `OfferWeaponLevelUp` is off by default for that reason; turning
  it on needs `cardCount` raised to 4.
- **A dropped accessory that never lands is stranded.** If the pop arc somehow leaves the level
  entirely, `PopVelocity` is never removed and the guard stays `Airborne`. `OrbSpawnUtility`'s ground
  clamping makes this very unlikely, and it is not a dead end regardless - a Merchant repair still
  restores it (and destroys the stranded entity). Left as-is rather than adding a bespoke timeout,
  which is exactly the class of "safety timer" that would break real recovery.
- **An unassigned `DroppedAccessoryPrototype` degrades rather than bricks.** With no prototype to
  spawn, `TryBlock` still spends the durability point (the hit *was* blocked) but leaves the accessory
  worn, instead of parking the player in `Airborne` forever with no entity in the world to walk back
  to. It logs a `Log.Error` either way.
- **The service is Store-only.** Blacksmith would be the other plausible home; it's one `case` in
  `BlacksmithSystem` plus a card if that's ever wanted.
- **Every number is a decisive placeholder** pending a real balance pass, same convention every other
  content generator in this codebase follows.

---

## Current status

The code compiles once Quantum's codegen picks up the new/changed `.qtn` files
(`Accessory/AccessoryGuard.qtn` - a new file with two components and an enum - and `Events.qtn`'s five
new events); `SystemSetup.User.cs` registers `AccessoryGuardSystem` inside `GameplaySystemGroup`
alongside the other pickup systems, and `CommandSetup.User.cs` registers
`BuyAccessoryServiceCommand`. Simulation, View and Editor sources were syntax-checked; **nothing has
been verified in-Editor yet**, and no assets are authored.

### Editor authoring needed

1. Run `Tools > RiftRaiders > Generate Accessory Guard Content` to author
   `Assets/_QuantumUser/Resources/Accessory/AccessoryGuardConfig.asset`.
2. **Assign `RuntimeConfig.AccessoryGuardConfig`** (`QuantumMenuConfig.asset`), same place
   `ReviveConfig`/`CursedRiftConfig`/`StoreConfig` already are. Until this is assigned the whole
   mechanic is off - nothing is seeded, so nothing blocks.
3. Build **one** shared `DroppedAccessory` `EntityPrototype` and assign it to
   `RuntimeConfig.Prefabs.DroppedAccessoryPrototype`. Easiest route is to duplicate
   `Assets/_QuantumUser/Entities/Prefabs/CoinOrb.prefab` and swap `QPrototypeCurrencyOrb` for
   `QPrototypeDroppedAccessory`, then put a `DroppedAccessoryView` on the sprite child. It needs
   exactly three Quantum components:
   - `Transform3D` (implicit on any `QuantumEntityPrototype`),
   - **`GroundOffset`** - not optional: `PopMotionSystem`'s filter is
     `Transform3D + GroundOffset + PopVelocity`, so without it the pop arc is never integrated, the
     accessory stays `Airborne` forever and can never be recovered,
   - `DroppedAccessory`.

   No collider, no `DestroyAfterTime` - pickup is a plain distance check in `AccessoryGuardSystem`,
   and the accessory deliberately never expires. **Remove the `Billboard` from the sprite child** if
   you duplicated CoinOrb, or the airborne spin won't be visible (see "Spin while airborne").
4. Per hero, author `CharacterData.Accessory` (`DisplayName` / `CollectibleSprite`) on that hero's
   own `CharacterData` asset.
   `Assets/_Project/Art/Sprites/Character/Heroes/Geometric/MaxGeometricHat.png` is already imported
   for Max.
5. Per hero, add an `AccessoryView` component to that hero's View prefab and assign the two
   GameObjects it switches between - `equippedVisual` (e.g. a `head_0` variant wearing the cap) and
   `unequippedVisual` (the bare-headed one). Both are optional; the `Preview Equipped` /
   `Preview Dropped / Broken` buttons check the swap in-scene without a live match.
6. Optional polish, all null-safe and skippable:
   - `EffectsManager.accessoryBrokenEffectPrefab` / `accessoryRecoveredEffectPrefab` - the shatter
     particle where broken debris lands, and the pickup burst where it's recovered. Empty simply
     plays nothing (deliberately no explosion fallback for either).
   - `HitFeedback.blockFlashColor` / `recoverFlashColor` / `HurtOverlayUiWidget.blockHitStopDuration`
     - all three already have working defaults.
   - The break toast needs nothing authored, as long as the gameplay scene has its `ToastManager`
     with pooled `ToastWidget`s - without one the call no-ops safely.
   - `CharacterUiWidget.accessoryEquippedRoot` + `accessoryGuardPips[]` - author 3 pips to match
     `BaseDurability`. `accessoryGuardRoot` (the strip holding them) is already wired on
     `CharacterUiWidget.prefab`.
   - `MovementRingView.accessoryArrowSprite` (+ optional `accessoryIconSprite`) - the radar arrow
     (see "Radar" above). Duplicate the hero prefab's existing `TargetArrow` sprite child under the
     same ring root, assign it here, and set `accessoryArrowRotationOffset` the same way the other
     arrows are for that art. The icon needs no sprite authored on the prefab - it is painted from
     `CharacterData.Accessory.CollectibleSprite` at spawn. Left unassigned nothing shows and nothing
     breaks. Optionally add an `EntityViewCacheInit` to the dropped-accessory prototype so the arrow
     tracks its interpolated view rather than its simulation position.
7. `ChooseWindow.cardCount` only needs raising to 4 if you turn `StoreConfig.OfferWeaponLevelUp`
   back on - the default row (2 food + accessory service) fits the stock 3 slots. `RefreshStore` now
   logs a one-shot warning naming the shortfall if it is ever handed more cards than it has slots.
8. Nothing else. Durability, blocking, dropping, recovery and pricing are all hero-agnostic and driven
   entirely by the config asset from step 1.

---

# 2026-08-25 — Shield reworked into the Accessory's protective layer

Shield used to be a free, continuously regenerating absorb pool sitting on top of the accessory. That
blunted the whole economic decision this feature exists to create: if a bar refills itself every five
seconds, a durability point you must pay a Merchant to restore is not really the layer keeping you
alive. Shield now has a different job, and it is defined in terms of this feature.

## The three rules

1. **Player Shield never auto-recharges** (`Shield.ChargeOnly`, seeded from
   `CharacterData.ShieldChargeOnly`, on for all six heroes). It starts a run **empty** and is only ever
   filled by an ability, a teammate or a purchase.
2. **Shield protects the accessory, up to what it can actually soak.** `DamageUtility.ApplyDamage`
   skips `AccessoryGuardUtility.TryBlock` only while the target's `Shield.Current` fully covers the
   incoming hit — see "2026-08-29 — Shield only covers what it can afford" below, which supersedes the
   original "any Shield at all" gate this section originally shipped with.
3. **Overshield is gone.** `ShieldUtility.ApplyOvershield` and every `OvershieldCapMultiplier` were
   deleted; all grants cap at `Max`. The above-Max concept only existed because a self-refilling pool
   needed something to make a grant feel meaningful — a charge-only pool is already scarce enough.

## Why the gate, and not moving the hook

The obvious reading of "the guard is a hit directly into Health" is to move the block hook below
`AbsorbWithShield`. That was rejected: the hook sits at the very top of `ApplyDamage` deliberately, so
a blocked hit rolls no crit, applies no elemental proc, builds no Rage/Resonance and fires no
`OnWeaponHitLanded`/`OnHealthDamageApplied`. All of that happens between the two points, so moving the
hook would forfeit every one of those guarantees — and a 1-damage overflow would burn a whole
durability point, meaning chip damage could eat hats.

Gating instead produces the same player-visible behaviour with none of that cost, and is hero-agnostic
for free. **As of 2026-08-29 the gate compares magnitude, not just presence** — see the section below
for why "any Shield at all" turned out to be the wrong question.

## Guardrails

`RechargeRate = 0` was NOT how this got expressed. Two error paths actively fight that value -
`CharacterSystem.SeedShield` (once at spawn) and `ShieldSystem` (once per second, for the whole run) -
and both are worth keeping for anything that genuinely should recharge, since they catch real
authoring mistakes. `ChargeOnly` suppresses both, and only for entities that opted in.

**Enemies and bosses are untouched.** `EnemySystem.SeedShield` never sets the flag, so the Shielder
enemy, the `ShieldWall` Director group and `BossWidget`'s shield bar all keep classic recharging
behaviour.

## Consequences worth knowing

- **Kai, Pixie and Max have no Shield route of their own** and will sit at 0 unless a teammate or the
  Store supplies one. Shield is now a *team* resource; the accessory is personal defence.
- **The standing Shield sources are:** Brute's Juggernaut Discharge (self), Brute's Bodyguard rank 2+
  (self, on a save), Zara's Protective Rhythm (allies), Zara's Encore overheal conversion, Lux's Sentry
  Shield Battery, and the Store's Shield Cell.
- **Not yet retuned, deliberately** (each is a balance call, not a code one): Rift Mutations Glass Core
  (2x Max Shield now only raises a cap you must fill), Last Bastion (trading Shield away is now nearly
  costless), Infinite Momentum (its 10-Shield dash cost is now usually a Health cost) and Shield Breaker
  (its `OnShieldBroken` trigger is now rare); the `PlayerMaxShieldLevel` talent, now a cap-raiser
  rather than protection; and
  `StatusEffects.ShieldRegen*` / `HasShieldRegenBuff` / `ShieldRegenBuffView`, which are now
  player-dead.
- **Retuned since (2026-08-27):** the "+10 Max Shield" Global Upgrade was replaced outright by
  **Toughness** ("-10% Damage Taken", a compounding `CharacterStats.DamageTakenMultiplier`) - a
  repeatable pick that raised a cap the player can no longer reliably fill had stopped reading as
  survivability. See `docs/global-upgrades.md`'s "Toughness replaces Shield" section.
- `ShieldUiWidget` lost its above-Max colour swap (nothing can exceed Max any more), and
  `CharacterUiWidget`'s recharge shine no longer fires for players.

## Lux is the one exception

`ShieldChargeOnly` is authored **false on Lux only**. A self-recharging barrier is the one hero
fantasy that genuinely fits it - he is the engineer, and his Sentry already grants Shield to allies
(Shield Battery). Every other hero is charge-only. Enemies never carry the flag at all.

Practically this makes Lux the only hero whose Accessory is passively protected: his Shield refills
5s after he stops taking hits (50 Max at 10/sec), so his hat only comes off under sustained pressure.
That is a real asymmetry and it is deliberate - but it is also the first thing to look at if Lux ends
up losing his accessory far less than anyone else.

## Do NOT trust C# field-initializer defaults on existing assets

`ShieldChargeOnly` is authored explicitly (`1`/`0`) on all six hero assets rather than left to its
`= true` C# initializer, because that initializer proved unreliable in practice:

- `BodyguardSkillAction.asset` picked up its four brand-new fields from their C# initializers
  **correctly** (`GuardDuration` 2.5/3.5/3.5, `ShieldReward` 0/10/15, `ShockwaveRadius` 3,
  `ShockwaveForce` 4).
- `PixieCharacterData.asset` picked up `ShieldChargeOnly` as **`0`** - the exact opposite of its
  `= true` initializer.

Same Unity version, same session, opposite outcomes. Whatever the mechanism, the lesson is that a new
field's value on a pre-existing asset must be verified on disk, never assumed - which is the same
warning `BruteAscensionAssetGenerator`'s own header already gives for changed field TYPES. Had this
gone unnoticed, every hero would have silently kept a classic auto-recharging Shield and the entire
rework would have been a no-op at runtime.

---

# 2026-08-29 — Shield only covers what it can afford

The "any Shield at all skips the accessory" gate from the 2026-08-25 rework, above, meant a hit that
blew straight through a small remaining Shield still spilled its overflow onto Health with the
accessory sitting there untouched — confirmed with the user as the wrong feel: a hit big enough to
overwhelm your Shield should cost you the hat, not your life.

**The rule now:** `DamageUtility.ApplyDamage`'s gate (still the same call site, `DamageUtility.cs`
right above `AccessoryGuardUtility.TryBlock`) compares the incoming hit's raw magnitude against
`Shield.Current` instead of just checking whether it's nonzero — `ShieldFullyCoversDamage(f, target,
damage)` (renamed from `HasShieldRemaining`), `shield->Current >= damage`. Comparison is against the
*raw* pre-crit, pre-armor `damage` passed into `ApplyDamage`, the same value `TryBlock` itself is
called with — the block hook runs before crit/armor resolution regardless, so there is no
"post-mitigation" number available yet to compare against instead.

- **Shield fully covers the hit** (`Current >= damage`): unchanged from 2026-08-25 - the accessory
  sits out, `AbsorbWithShield` drains the Shield by exactly `damage`, Health takes nothing.
- **Shield does NOT fully cover the hit** (`Current < damage`, including a Shield of 0 or no Shield
  component at all): `AccessoryGuardUtility.TryBlock` runs instead. If it succeeds, this is a full
  negation — same "no crit roll, no elemental proc, no Rage/Resonance, no
  OnWeaponHitLanded/OnHealthDamageApplied" guarantee the 2026-08-25 section already established for
  every accessory block — and **the Shield is untouched**, not drained. The durability point is what
  paid for the hit, not the Shield.
- If the accessory can't block (already `Broken`, `Disabled`, or `CurrentDurability == 0`), execution
  falls through to the normal path: `AbsorbWithShield` drains whatever Shield there is and the
  remainder lands on Health, exactly like the old always-overflow-to-Health behaviour. The accessory
  gate is a first refusal, not a guarantee — a target with no working accessory left still needs
  Shield/Health to fall back on.

**Why leave Shield untouched on a successful block, rather than draining it first and blocking only
the remainder:** `TryBlock` is architecturally all-or-nothing — it returns before any of `ApplyDamage`'s
resolution steps run, which is the entire point of the negation guarantee above. Splitting the hit into
a "Shield eats part, accessory eats the rest" hybrid would mean partially resolving the hit (draining
Shield, firing `OnShieldDamageApplied`) while also fully negating it (no crit, no Health), which is a
contradiction the rest of the pipeline isn't built to express. Full negation keeps the guarantee simple:
either Shield alone was enough and the hit resolves as absorbed, or it wasn't and the accessory eats the
*entire* hit, Shield included. A side effect worth knowing: a hit that blows through a nearly-full
Shield still costs a whole durability point rather than draining that Shield down first — the same
"partial credit doesn't exist" trade the 2026-08-25 gate always made, just flipped to favor the
accessory instead of Health.

---

# 2026-08-30 — Minimum damage threshold to block (chip damage no longer costs durability)

Flagged by the user: a block is tudo-ou-nada (full negation, same durability cost) regardless of the
hit's size, so a Filler/Swarm enemy tapping for 1 damage drained the exact same Coin-priced durability
point as a Heavy landing a real hit. In a swarm-heavy fight that made the accessory's economy bleed out
fast for almost no defensive value, while against one big hit it was clearly worth it.

**The fix:** a new `AccessoryGuardConfig.MinDamageToBlock` (`FP`, default `0`). `AccessoryGuardUtility.
TryBlock` checks it right after the existing `State`/`CurrentDurability` early-returns and *before*
decrementing durability - a hit dealing less than the threshold returns `false` immediately, so it falls
straight through to `DamageUtility.ApplyDamage`'s normal resolution (crit roll, procs, Health) exactly
as if no accessory existed for that one hit. Nothing is spent, nothing pops off. `0` (the default)
reproduces the original "block everything" behaviour exactly, so every existing call site/asset is
unaffected until a nonzero value is authored - same "opt-in, no behaviour change until set" convention
`EncounterModifierUtility`'s bonuses and `LevelUpConfig.LevelSequence` already use.

Deliberately compared against the same raw pre-crit/pre-armor `damage` the block hook already receives
(the value `TryBlock` was always called with) - no new resolution step, no new parameter threaded
through `ApplyDamage`. `AccessoryGuardContentGenerator` now authors a decisive placeholder of `3`
(Filler/Swarm chip damage is typically 1-2, a real hit from Normal-tier-or-above is expected to clear
it) - pending a real balance pass once actual per-tier damage numbers are tuned, same convention every
other number in this file follows.

**Known simplification:** the threshold is a single flat number, not per-tier or per-source. A weapon
perk or mutation that deals unusually small direct-hit damage would also slip under it - accepted, since
introducing a second axis (which *sources* count) for an edge case with no reported instance yet would
be exactly the kind of premature generality this codebase avoids elsewhere.

**Editor authoring needed:** re-run `Tools > RiftRaiders > Generate Accessory Guard Content` (or edit
`AccessoryGuardConfig.asset` by hand) to actually author the new `3` value - the existing asset on disk
predates this field and will keep resolving to `0`/disabled until then.

---
