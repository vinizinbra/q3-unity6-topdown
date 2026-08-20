# Lux — Ascensions (Engineer / Sentry Architect / Scrap Engine)

Lux's first real Ascension pass (2026-08-20). Her fantasy is now an explicit loop:

**Deploy → fight around the Sentry → collect Scrap → improve/recycle → reposition/redeploy.**

She starts with a genuinely simple machine and Ascensions turn it into a weapons platform. That arc is
the whole design, and it's why the old fully-loaded baseline had to go.

Read `docs/level-up-upgrades.md` first if you haven't touched a ranked Ascension line before —
`IRankedUpgrade`/`MaxRank`/`SkillUpgradeUtility.GetRank`/`RankDescriptions` are established there.

---

## Roster: 9 lines × 3 ranks

| Pool | Lines |
|---|---|
| Sentry (Hero Skill) | Weapon Systems, Overclock, Fortification, Overload Core |
| Scrap (Passive) | Scavenger, Rapid Recycling, Field Modifications |
| Dash | Emergency Repair, Relocation Protocol |

---

## Baseline: deliberately minimal

`SpawnSentrySkillAction` now deploys **one Cannon, 3 range, 10s life, 20s cooldown** — no shield, no
aura, no fire-rate bonus, no on-death explosion. Removed from the baseline and turned into Ascensions:
Rocket, Minigun, Laser, Shield Battery, Fire Support, Overclock, Extended Range, Overload Core.

The baseline Cannon is armed into **slot 0** (`BaselineWeapon`/`BaselineWeaponOffset`), which matters
because MK II later swaps exactly that slot. Weapon Systems owns slots 1-3.

### Lifetime is Health, not a timer

A Sentry's `Health` drains at `Sentry.DecayRate` (computed once at deploy as `MaxHealth / Duration`),
so "ran out of time" and "killed in combat" are the same death path. That was already true, and this
pass leans on it hard: **remaining lifetime is `CurrentHealth / DecayRate`**, extending lifetime is
adding `DecayRate * seconds` of Health, and repairing a fraction of Max Health *is* giving back that
fraction of its life. One source of truth, no second timer, and the health bar keeps doubling as a
time-remaining gauge. See `SentryUtility`.

### Fire rate composes in one place

`Sentry.FireRateMultiplier` (permanent: Overclock, Field Modification stacks) × `TempFireRateMultiplier`
(timed: Emergency Overclock, Rapid Setup) × Redline. `SentryBarrelSystem` recomposes each barrel's
`Weapon.FireCooldownMultiplier` from `SentryBarrel.BaseFireCooldownMultiplier` **every tick** — against
the captured baseline, never the live value, which is what makes a per-tick write idempotent instead of
compounding, and lets a timed buff simply lapse with no revert logic. `SentryUtility.ResolveFireRateMultiplier`
is the single resolution point.

---

## Base Passive: Scrap Collector

- Normal-tier-and-up kills have a 25% chance to drop Scrap. Filler drops nothing until Scavenger.
- Collecting **10** Scrap grants a **Fabrication Charge** — the Hero Skill's next cast is free
  regardless of remaining cooldown (`SkillSystem.GrantFreeCast`). It resets only when the free cast is
  actually *spent*, not when the threshold is reached.
- **Max 1 stored Fabrication Charge** — enforced by `GrantFreeCast`'s own no-op-if-already-pending.
- **Max 2 active Sentries per Lux** (`LuxScrapCollector.MaxActiveSentries`). Deploying past it silently
  retires her oldest (`DespawnIntentUtility.DespawnSilently(Replaced)`) rather than refusing the cast.

Those two caps together are what makes a Sentry → kill → Scrap → Sentry runaway structurally
impossible: extra Charges can't bank, and extra Sentries can't accumulate.

**Co-op isolation:** every count lives on `LuxScrapCollector`, which lives on Lux herself, and every
sentry lookup is scoped by `Sentry.Owner`. Two Luxes have entirely separate Scrap progressions,
Charges, Sentry caps, Field Modification stacks and relocation ownership, with no shared state
anywhere. A kill landed by a barrel is traced back to its owning Lux via
`SentryBarrel.Sentry → Sentry.Owner` (`ScrapUtility.ResolveRealOwner`).

---

## Sentry lines

**1. Weapon Systems** (`SentryWeaponSystemsSkillAction`) — R1 Minigun, R2 + Rocket Pod, R3 + Laser
("Full Arsenal": Cannon + Minigun + Rockets + Laser firing at once).

Each is an ordinary `WeaponDataAsset` armed into its own `SentryBarrel` entity. "Periodic burst",
"periodic AoE rocket" and "piercing laser" are therefore *authored weapon data* — fire rate, burst,
projectile, pierce — running through the same `WeaponSystem` as every other gun. Because each barrel is
its own Weapon-carrying entity with its own cooldown and its own independently-chosen target, "all
weapon systems may operate independently according to configured cooldowns" needs no scheduler at all.
The line SETS the full loadout every activation rather than accumulating, so a lower rank can't leave a
higher rank's slot armed.

**2. Overclock** (`SentryOverclockSkillAction`) — R1 +25% Fire Rate; R2 +40% and +2s life; R3 "Redline"
+50% and a further +100% during its final 3s.

Redline latches ON the first time *remaining* lifetime crosses the threshold and stays on until death —
deliberately the simple one-way behavior the brief prefers, so extending lifetime afterwards (Emergency
Repair, Relocation) reads as a synergy rather than a trap, with no oscillation to reason about.
`SentryDecaySystem` does the latching.

**3. Fortification** (`SentryFortificationSkillAction`) — R1 "Extended Range" +2; R2 "Shield Battery"
3 Shield/sec to allies in the aura; R3 "Fire Support" +15% Fire Rate and 10% Damage Reduction.

Shield Battery is a **flat per-second amount**, replacing the old "multiply the ally's own shield
recharge rate by 100" (which scaled with the recipient and was effectively unbounded). Fire Support is
one authored `AllyBuffEffectData` — the same generic effect Zara's Support Beat uses — so its Damage
Reduction lands in the single shared aura-DR slot (`StatusEffects.AuraDamageReductionRemaining`,
take-the-stronger). That is what makes "buffs from multiple Sentries must not stack" and "Guardian +
Fire Support must not compound" true by construction rather than by a per-source check. The aura reaches
`Sentry.Range * AuraRangeRatio` (half), deliberately tighter than targeting range — "fight around the
machine" should mean genuinely standing near it.

**4. Overload Core** (`SentryOverloadCoreSkillAction`) — R1 100% Sentry Skill Damage in 4m; R2 175%,
+30% radius, strong knockback; R3 "Critical Meltdown" 250% and enemies caught become **Exposed**
(+20% damage taken, 3s).

Exposed reuses the pre-existing generic **Rupture** status (`StatusEffectUtility.ApplyRupture`) rather
than a new Lux-specific one — it is already "incoming damage multiplier, take-the-stronger", so it
composes with everything that reads it and two meltdowns don't stack additively.

**Despawn reason tagging:** it fires only from Health genuinely reaching 0. A sentry retired for
housekeeping — replaced past the active cap, or picked up by Relocation Protocol — is despawned with an
explicit `DespawnIntent`, which `DamageUtility.TrySentryOverload` rejects. Without that, redeploy-spam
would be the cheapest way to trigger it.

---

## Scrap lines

**5. Scavenger** — R1 Filler enemies start dropping (10%, their own separate chance); R2 all chances
+~25%; R3 "Jackpot" Specialist/Heavy/Elite always drop ≥1, Boss drops 3 (configured separately so it
can be tuned on its own).

**6. Rapid Recycling** — R1 −0.5s remaining Sentry cooldown per Scrap; R2 −1s; R3 "Instant Assembly"
a further −3s at the moment a Fabrication Charge is earned. Kept deliberately separate from the Charge
itself: a Charge is a *free deploy regardless of cooldown*, this *reduces* the cooldown; both being live
at once is rank 3's actual payoff. Every reduction clamps at 0 and never banks.

**7. Field Modifications** — R1 +4% Sentry Damage per Scrap collected while a Sentry is active, max 5
stacks; R2 each stack also +3% Fire Rate; R3 "MK II" at max stacks the Cannon becomes a Twin Cannon.

This is the line that makes the loop *active*: deploy, then feed the machine before it expires. Stacks
live on the **Sentry** (`SentryModifications`), not on Lux — so they last for that machine's lifetime
and reset on redeploy, exactly as specified. With several Sentries active, Scrap goes to the **most
recently deployed** one (`SentryUtility.FindNewestOwnedSentry`), the brief's own preferred starting rule,
deliberately not "buff every Sentry at once".

MK II is a **weapon swap**, not a second turret implementation: the slot-0 barrel is re-equipped with a
different `WeaponDataAsset` through the ordinary `WeaponSystem.Equip` path. Because Equip re-seeds that
barrel's stats, `ScrapUtility.TryApplyMkII` re-applies every damage stack earned so far on top of the
new weapon's baseline, so the swap is always an upgrade and never a silent reset.

---

## Dash lines

**8. Emergency Repair** — R1 ending a dash within 6m of her own Sentry repairs 30% of its Max Health
(= 30% of its lifetime); R2 also extends its remaining lifetime by 2s; R3 "Emergency Overclock" also
grants it +50% Fire Rate for 2s.

The extension is drawn from that **Sentry's own capped allowance** (`Sentry.LifetimeExtensionRemaining`,
seeded at deploy from `SentryLifetimeExtensionBudget`, 4s). Capping per-sentry rather than per-Lux is
what stops a dash-cooldown build from keeping one machine alive indefinitely while still letting her
service each new one. Relocation Protocol R2 draws on the *same* allowance, so holding both lines
doesn't double it.

**9. Relocation Protocol** — R1 "Reposition" dashing from within 4m of her own Sentry **moves** it to
the dash destination; R2 "Rapid Setup" +25% Fire Rate for 2s and +1s lifetime; R3 "Hot Drop" an
immediate volley and radial knockback pulse on landing.

Deliberately **not** Decoy Beacon (the brief rules that out). State preservation — HP, remaining
lifetime, Field Modification stacks, weapon modules, aura upgrades, Overload Core, Redline — is free
because the sentry is *moved*, not destroyed and re-created: the same entity and every component on it
survive, and its barrels re-anchor to the chassis on their own next tick (`SentryBarrelSystem`
re-derives barrel positions every tick, not just at spawn).

The candidate Sentry is **latched at dash Begin** (`SentryRelocationPending`), not searched again at
End — that's what makes it read as "dash while standing at your Sentry and it comes with you" rather
than "dash anywhere and grab whatever's at the destination". Hot Drop is a plain damage+knockback
sweep; it re-runs no deployment trigger, so it cannot recurse into Overload Core or another Hot Drop.

---

## Removed

**Deleted outright:** `SentryAddWeaponSkillAction`, `SentryAddOverloadSkillAction`,
`SentryAddFireRateSkillAction`, `SentryAddShieldSkillAction`, `SentryAddShieldAreaRateSkillAction`,
`SentryIncreaseFireRateSkillAction`, `SentryIncreaseRangeSkillAction`, `PortableCoverSkillAction`,
`RepairNearbyMachinesSkillAction`, `EfficientSalvagePassiveUpgradeData`, `EnhacementPassiveUpgradeData`
(and its `LuxScrapCollector.MachineHealthBonusPerPickup` / `ScrapUtility.ApplyToOwnedSentry`), the
unfinished Decoy Beacon plan, and the seven single-effect components
(`SentryFireRateUpgrade`/`SentryFireRateAuraUpgrade`/`SentryShieldUpgrade`/`SentryShieldAreaRateUpgrade`/
`SentryRangeUpgrade`/ the old `SentryWeaponUpgrade`/`SentryOverloadUpgrade` files) — consolidated into
one component per LINE in `Heroes/Lux/SentryUpgrades.qtn`.

**Rewritten in place:** `ScavengerPassiveUpgradeData`, `RapidRecyclingPassiveUpgradeData` (both were
single-pick, now ranked), `ScrapUtility`, `ScrapCollectorPassiveData`, `SpawnSentrySkillAction`,
`SentryAuraSystem`, `SentryDecaySystem`, `SentryBarrelSystem`.

---

## Current status / Editor authoring needed

The code compiles (Simulation, View and Editor assemblies all verified clean) once codegen picks up the
new/changed `.qtn` files, and `SystemSetup.User.cs` needs no new Lux registrations — `SentryAuraSystem`/
`SentryDecaySystem`/`SentryBarrelSystem` all keep their existing positions.

`Tools > RiftRaiders > Lux > Generate Ascension Assets` (replaces `LuxScrapAssetGenerator`) authors and
wires all 9 lines plus the baseline Sentry, fully replacing every list it touches. **Not yet run.**
After running it, still needed by hand:

1. Assign `ScrapConfig.asset` and a `ScrapOrb` `EntityPrototype` to `RuntimeConfig`'s
   `ScrapConfig`/`ScrapOrbPrototype` (`QuantumMenuConfig.asset`) — a pre-existing gap, not new.
2. **Author dedicated sentry weapon data.** Minigun / Rocket Pod / Laser currently reuse
   SMG / GrenadeLauncher / BeamGun as decisive placeholders so the line is playable immediately.
3. **Author a real Twin Cannon** (2 projectiles at ~70% damage each) for MK II — it currently reuses
   the baseline Cannon, so MK II is a visual/event no-op until that exists.
4. Delete the stale `EfficientSalvage.asset` / `Enhacement.asset` / `PortableCoverSkillAction.asset`
   files.
5. View wiring for the four new events (`SentryRepaired`, `SentryRelocated`, `SentryRedlineEngaged`,
   `SentryUpgradedToMkII`) — all optional cues; nothing breaks without them.

Every numeric value not explicitly pinned by the brief is a decisive placeholder pending a real balance
pass. Not yet manually verified end-to-end in-Editor.

**Playtest first:** Scrap generation rate and Fabrication Charge frequency; whether 2 concurrent
Sentries is the right cap; Field Modification acquisition rate (5 stacks should be reachable within one
Sentry's life, but not trivially); Redline + Overload Core together; two Luxes in one match.

---

# 2026-08-20 — Sentry tentacle now grips the gun, not the barrel entity

Reported from testing: the sentry's tentacle "hand" wasn't picking up / holding its gun.

## Cause

`SentryView.OnSentryBarrelSpawned` pinned the same-indexed tentacle leg with:

```csharp
tentacleWalker?.SetPinnedTarget(e.SlotIndex, barrelView.transform);
```

`barrelView.transform` is the barrel **entity's root** — the transform `QuantumEntityView` writes the
raw simulated `Transform3D.Position` onto every frame. None of the gun's *presentation* lives there:
`SentryGunView.QUpdate` applies the aim rotation, the left-facing flip, the idle float and the shoot
punch to **`visualRoot`, a child**.

So the tentacle reached for a fixed point while the gun rotated, bobbed and recoiled around it — the
leg gestured *near* the gun instead of holding it, and the mismatch grew with every idle bob and every
shot.

## Fix

`SentryGunView` gained a `gripAnchor` Transform (authored as a **child of `visualRoot`**, so it
inherits rotation / flip / idle float / punch) exposed as `GripAnchor`, and `SentryView` now pins the
tentacle to that instead of the entity root.

This is the sentry's counterpart to `WeaponHandGripView`, which resolves the player's hand grip through
the **weapon's own live transform** (`WeaponView.RightHandGripPosition` = `transform.TransformPoint(
anim.rightHandGrip)`) for exactly the same reason — the hand has to share the gun's frame, not the
character's.

Two levels of graceful fallback, so nothing breaks unauthored:

- No `gripAnchor` on the gun → falls back to `visualRoot`. Still tracks the gun's motion correctly,
  just grabs its pivot rather than a hand-placed spot on it.
- No `SentryGunView` at all (a barrel prefab that isn't a gun) → falls back to the barrel root, exactly
  the old behavior.

## Depth offset

The tentacle is pinned to a **runtime child** of the authored anchor (`GripPoint`, created in `Awake`),
never the authored transform itself, carrying `gripZOffset` (default **-0.02**) on its local Z.
`visualRoot` is billboarded to face the camera, so its local +Z runs away from the viewer and a negative
value pulls the hand toward it - the tip draws in front of the gun sprite instead of z-fighting with it
or vanishing behind it.

The extra child is what makes that offset safe: when no `gripAnchor` is authored the anchor falls back
to `visualRoot`, and nudging *that* would move the entire gun sprite rather than just the hand.

## Editor authoring

Optional but wanted for a good result: add an empty child under `SentryBarrel.prefab`'s `visualRoot`,
place it where the tentacle should grab the gun, and assign it to `SentryGunView.gripAnchor`. Without
it the tentacle grabs the gun's pivot, which already tracks — it just may not be where the art wants a
hand.

## Related, not fixed here

The sentry gun's idle motion is still its own thing (`SentryGunView.IntegrateIdleFloat`, two randomized
sine waves) and is unrelated to the player's gun idle. The player's gun has no idle *animation* at all -
no weapon `ViewPrefab` carries an Animator; the motion comes from `PlayerGunAimView`'s **Body Follow**
layer bleeding `BlobAnimationView`'s torso lean/rock/bob into the gun. There is no `BlobAnimationView`
on `Sentry.prefab` or `SentryBarrel.prefab`, so a sentry has no body motion to bleed in - which is
exactly why `SentryGunView` was given a standalone float instead. Making the two match would mean
either giving the chassis a `BlobAnimationView` to follow, or hand-tuning the float to resemble it.

---

# 2026-08-20 — Skill Area now scales the sentry's range

`SpawnSentrySkillAction` set `sentry->Range` from its own authored `Range` plus Fortification rank 1's
additive Extended Range bonus, and nothing else - so the generic **Skill Area** global upgrade
(`CharacterStats.AreaRadiusMultiplier`, via `StatUtility.GetAreaMultiplier`) had no effect on a turret,
even though it scales essentially every other area in the game.

Now:

```csharp
sentry->Range = (Range + ResolveRangeBonus(f, filter.Entity))
    * slot->AreaMultiplier * StatUtility.GetAreaMultiplier(f, filter.Entity);
```

Composing `slot->AreaMultiplier` with `StatUtility.GetAreaMultiplier` is the same pairing
`HitPathSkillAction`, `SpawnEntitySkillAction` and `AreaHitData.Detonate` already use, so a Focused
Power-style per-slot multiplier reaches the sentry too, not just the global upgrade.

Applied **after** Extended Range's additive bonus, so the percentage covers the ascension's
contribution rather than only the authored baseline - the same ordering Brute's Aftershock uses for its
own stack-radius bonus.

`Sentry.Range` is the single value every consumer reads live, so nothing else needed changing:

| Consumer | Reads |
|---|---|
| `SentryBarrelSystem` | targeting range (`TryFindNearestEnemy`) |
| `SentryAuraSystem` | Fortification aura reach (`Range * AuraRangeRatio`) |
| `SentryRangeIndicatorView` | the ring the player sees |
| `SentryView` | the Shield Battery / Fire Support aura particle sizes |

It is also the **only** writer of `Sentry.Range` anywhere, so a relocated or Mk II-upgraded sentry keeps
its scaled range.

**Baked at deploy time, deliberately.** A Skill Area upgrade picked up *after* a sentry is already out
does not retroactively grow it - same "resolved once at spawn" convention the vortex's own radius,
enemy stats and every other spawn-configuring upgrade in this codebase already follow. The next deploy
picks it up.

---

# 2026-08-20 — Relocation Protocol: airborne re-grounding + carry-during-dash

Two fixes to the same line, both about the moment the Sentry actually moves.

## The Sentry hung in the air

`RelocationProtocolSkillAction` moved the machine with a bare `sentryTransform->Position = destination`,
where `destination` is wherever **Lux** was standing - which is easily mid-air when she dashes off a
ledge or over a gap. Nothing re-grounded it, so it stayed at her Y for the rest of its life.

It now calls `GroundOffsetUtility.Apply` right after the move: the same ground resolve every spawn
already runs. `Sentry.prefab` already authors a `GroundOffset` with `FallGravityMultiplier = 1`, so the
turret **drops under real accelerating gravity** via `SettlingToGround`/`GroundSettleSystem` rather than
snapping. No new authoring needed.

That also exposed a latent bug in the shared helper: `GroundOffsetUtility.Apply` did a plain
`f.Add<SettlingToGround>`, which silently keeps a **stale `TargetY`** if the entity has settled before -
fine when it only ever ran once at spawn, wrong the moment anything re-grounds mid-life. It is now
`AddOrGet` with both fields written explicitly, `FallVelocity` reset so a fresh drop accelerates from
rest instead of inheriting the previous fall's speed.

## The Sentry teleported instead of travelling

The relocation only happened on `SkillActionPhase.End`, so the machine vanished and reappeared at the
destination once the dash finished - the feedback landed a beat after the press.

The action now also runs `OnGoing` (`Interval = 0`, every tick) and drags the latched Sentry to Lux's
live position each tick, so it visibly rides along with her for the whole dash. It ends in exactly the
same place as before; only the travel is now shown rather than skipped.

The split is strict, and that is the point:

| Phase | Owns |
|---|---|
| `Begin` | publish the extension budget, latch **which** Sentry is close enough to come along |
| `OnGoing` | position only - nothing else |
| `End` | the Fire Rate burst, the lifetime extension, the Hot Drop blast, the `SentryRelocated` event, and the ground re-settle |

So no one-shot payload can fire repeatedly mid-dash. `OnGoing` also deliberately does **not** clear the
latch (`End` still needs it) and does not re-ground (the machine is being carried, not dropped).

Barrels needed no handling at all - `SentryBarrelSystem` already re-anchors them off the chassis every
tick. An interrupted dash (stopped by a wall) still fires `End`, so it lands correctly either way, and a
Sentry destroyed mid-dash is covered by the existing `f.Exists` guard.
