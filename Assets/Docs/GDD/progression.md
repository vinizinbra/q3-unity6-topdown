# Run Progression (not yet built)

This is a game-design doc (run/level progression); the implementation docs it references live
separately under `Assets/_QuantumUser/Docs/` since they're code/architecture references, not design.

See [skills.md](../../_QuantumUser/Docs/skills.md) for `SkillData`/`SkillActionData` (the
polymorphic-asset idiom this doc extends a third way),
[weapons.md](../../_QuantumUser/Docs/weapons.md) for weapon data/view resolution, and
[architecture.md](../../_QuantumUser/Docs/architecture.md) for the Simulation/View split.

Survivors.io/Gunfire Reborn-style run progression: on level-up, the player picks one of a few
rolled options; every pick is **permanent for the rest of the run** (no timed buffs, no expiry —
simpler than a generic buff system, since nothing ever reverts mid-run).

## Two upgrade shapes, not one

Picks split into two runtime shapes because they behave differently — forcing both through one
mechanism either bloats the simple case or under-powers the complex one.

| Shape | Examples | Mechanism |
|---|---|---|
| **Stat delta** | Damage %, fire rate %, move speed %, max HP %, armor, health regen, dash charges +1, dash recharge speed %, pickup radius, XP gain % | Applied once on pick: a single mutation to a `CharacterStats` component (or, for dash charges specifically, `SkillSlot.MaxStacks` — see [skills.md's "Charges, not a cooldown lock"](../../_QuantumUser/Docs/skills.md#charges-not-a-cooldown-lock), which already documents this field as upgrade-mutable). No hook, no per-tick cost, nothing to register. |
| **Autonomous passive** | Slow aura, periodic area damage, random bomb throw, orbiting blades, life-on-kill, thorns/retaliation | Needs actual runtime behavior with no player input and no AI state machine driving it — the gap neither `AttackData` (AI-driven) nor `SkillData` (input-driven) covers. New `PassiveAbilityData` polymorphic asset, third sibling in the same idiom. |

Don't route a pure stat delta through the passive-hook machinery below — it's needless indirection
for something that's just `stats->FireRateMultiplier += FP._0_10` once.

(DashSkill/HeroSkill leveling and the Hero Passive, below, add more *pool categories* — things a pick can
target — not a third runtime shape; both reuse one of the two mechanisms above.)

## Planned shape for autonomous passives

Mirrors `SkillData`/`SkillActionData` deliberately — same "polymorphic asset owns its own logic,
generic system just dispatches lifecycle calls" idiom used twice already, so this is a proven
pattern in this codebase rather than a new one.

- **`PassiveAbilityData : AssetObject`** (`Simulation/Assets/Passives/`) — one `virtual void
  Tick(Frame f, EntityRef owner, PassiveSlot* slot)` hook, no-op by default. Each concrete type
  (`SlowAuraPassiveData`, `AreaDamagePassiveData`, `RandomBombPassiveData`, ...) owns its own
  internal cooldown/pulse timing — a periodic passive checks its own `slot->CooldownTimer` and
  resets it after firing; a continuous one (slow aura) just re-applies every tick. The dispatching
  system never branches on passive type, same as `SkillSystem` never branching on skill type.
- **`PassiveSlot` runtime struct** — `AssetRef<PassiveAbilityData> Passive; FP CooldownTimer;` —
  paired with the asset the way `SkillSlot` pairs with `SkillData`. Lives in a `list PassiveSlot` (or
  a capped `array<PassiveSlot>[N]` if an upper bound is preferable to an unbounded qtn `list`) on the
  player, since count grows unpredictably over a run — unlike `CharacterSkills`' two fixed named
  slots, this is genuinely open-ended.
- **One new `PassiveSystem`** — `SystemMainThreadFilter` over players with equipped passives, calls
  `Tick()` on every slot every frame. Stays generic; all per-passive logic lives in the asset.
- **Reuse, don't duplicate**: bomb-throw/area-damage passives should call `ProjectileSpawner`/
  `DamageUtility`/`ExplosionUtility` for spawning and hit resolution, the same shared utilities
  `SkillActionData`/`AttackData` implementations already use — a passive deciding *when* to fire is
  the only new logic, not *how* to damage something.
- **Reactive hooks (`OnDamageDealt`/`OnDamageTaken`/`OnKill`) are deliberately NOT part of the first
  cut** — every idea below is expressible as pure `Tick()`. Add a reactive hook only once a specific
  idea needs one (e.g. life-on-kill, thorns) rather than speculatively wiring hooks nothing calls —
  the same reasoning `skills.md`'s "Chained skills" section applies to its own unbuilt extension.

## The full pick pool

Everything the level-up screen can offer, and where it lives:

| Pool entry | Shape | Slot | Status |
|---|---|---|---|
| Stat delta (+5% damage, ...) | Stat mutation | n/a — `CharacterStats` field | Mechanism trivial; most individual stats not wired yet, see [Stat deltas](#stat-deltas) |
| DashSkill / HeroSkill level up | `SkillData` asset swap | Fixed named slot (`CharacterSkills.DashSkill`/`HeroSkill`) | Swap mechanism already built (skills.md); per-level content not authored — see [Skill leveling template](#skill-leveling-template-dashskillheroskill) |
| HeroSkill unlock (one-time) | `SkillData` asset assigned into an empty slot | Fixed named slot (`CharacterSkills.HeroSkill`) | Slot exists and is empty; no second skill designed yet |
| Hero Passive level up | `PassiveAbilityData`-shaped, autonomous `Tick()` (+ reactive hooks as needed) | New **fixed** slot, one per character | Not designed — see [Hero Passive](#hero-passive-per-character-signature-ability--not-yet-designed) |
| Passive pool pick/level-up | Same `PassiveAbilityData` shape | Growable `list<PassiveSlot>` | Planned, not built — see above |
| Weapon level up | `WeaponDataAsset` swap | Equipped weapon slot | Swap mechanism exists per weapons.md; pool integration not designed |

DashSkill/HeroSkill leveling and the Hero Passive look mechanically like the generic passive pool (an
asset swap, or a `Tick()`-driven asset) — the actual dividing line isn't the runtime shape, it's
**fixed named slot** (exactly one, always there, baked per character) vs. **open-ended list** (grows
as the player picks up more generic passives during a run). Same distinction `CharacterSkills`
already draws between `DashSkill`/`HeroSkill` (fixed fields) and the planned passive pool (a `list`).

## Leveling an owned pick

Picking an already-owned upgrade again should follow the same convention `SkillData` already
documents: **one asset per level**, leveling = swapping which `AssetRef` a slot points at (e.g.
`SlowAuraLv2` — bigger radius/stronger slow), not stacking duplicate instances or a per-level
curve baked into one asset. Applies equally to stat-delta picks (re-picking "damage %" just applies
another flat delta, no leveling needed), passive picks (re-picking "area damage" swaps
`PassiveSlot.Passive` to the next-level asset), and skill/Hero Passive picks (see the next two
sections for the concrete per-level shape).

## Skill leveling template (DashSkill/HeroSkill)

No new mechanism needed beyond what skills.md already establishes: leveling a skill is swapping
which `AssetRef<SkillData>` a slot points at, and `SkillData.Actions` is a plain list — so "higher
level = does everything the previous level did, plus one more thing" is just a longer `Actions`
list on the higher-level asset, never a new field or a per-level curve baked into one asset. Six
assets per skill (Level 0 through Level 5), authored once, is the entire content cost — no
`SkillSystem`/`SkillData` code changes.

Two ways a level differs from the one below it, both plain Inspector edits on the new asset:
- **Add an action** — append one more `SkillActionData` entry to `Actions` (e.g. Level 1 adds
  `ExplodeSkillAction`, Level 2 additionally adds `KnockbackOnPathSkillAction`).
- **Tune a field** — bigger `ExplodeSkillAction.Radius`, shorter `SkillData.RechargeTime`, +1
  `InitStacks`, etc. on an asset that already carries the action from an earlier level.

### Example: DashSkill (Dash, traversal)

| Level | Asset | Actions (cumulative) | Notes |
|---|---|---|---|
| 0 | `DashLv0` | — | Plain dash, current shipped behavior |
| 1 | `DashLv1` | + `ExplodeSkillAction` (End) | Small nova on dash-end |
| 2 | `DashLv2` | + `KnockbackOnPathSkillAction` (OnGoing) | Enemies along the dash path also get knocked back |
| 3 | `DashLv3` | (tune only) | Same actions, bigger `ExplodeSkillAction.Radius` |
| 4 | `DashLv4` | + `InvulnerabilitySkillAction` (Begin \| End) | Dash also grants i-frames for its duration |
| 5 | `DashLv5` | (tune only) | Same actions, `RechargeTime` cut and/or `InitStacks` +1 |

### Example: HeroSkill (still unassigned — placeholder shape only)

| Level | Behavior sketch |
|---|---|
| 0 | Throw one projectile |
| 1 | Throw two projectiles (spread) |
| 2 | Bigger impact radius |
| 3 | Projectile pierces one extra enemy |
| 4 | Impact leaves a lingering damage-over-time patch |
| 5 | Throw count 3, or a distinct large "ultimate" payload |

HeroSkill itself isn't designed yet (skills.md's Roster still lists it `unassigned`) — this table only
shows that the same Level 0-5 template applies to whatever HeroSkill turns out to be, once a concrete
`SkillData` subclass is picked.

This implies pick-pool bookkeeping that doesn't have a home yet: the pool needs each slot's
*current* level, to roll only the next asset in sequence (never level N+2, never one already
passed) and stop offering that skill once at Level 5 — the same "how is the roll resolved" gap as
the first [open question](#open-questions) below, just applying to three more things (DashSkill,
HeroSkill, Hero Passive) instead of one.

## Hero Passive (per-character signature ability — not yet designed)

Distinct from the "Autonomous passive" pool above in *content*: a Hero Passive is bespoke to one
character — identity-defining, like a MOBA champion passive — not drawn from the same
slow-aura/area-damage/bomb/orbiting-blades list every hero can roll into. Mechanically there's no
reason to invent a second runtime shape for it, though — it reuses `PassiveAbilityData`'s
`Tick(Frame f, EntityRef owner, PassiveSlot* slot)` hook (and reactive hooks, once a concrete idea
needs one — see below), just addressed differently:

- **Fixed, named slot, not the growable list.** The generic passive pool lives in
  `list<PassiveSlot>` because its count is open-ended. A Hero Passive is exactly one per character,
  always present — same reasoning `CharacterSkills.DashSkill`/`HeroSkill` are fixed named fields instead
  of an array. Likely a new `AssetRef<PassiveAbilityData> HeroPassive` (+ its own
  `PassiveSlot`-shaped state) on `CharacterSkills` or a sibling component, not a special entry
  inside the growable list.
- **Always equipped from Level 0**, baked per character prototype (which concrete
  `PassiveAbilityData` subclass = this hero's identity) — same convention `DashSkill` is baked to
  `DashSkillData` today rather than picked at runtime.
- **Levels via the same pick pool** as everything else: "upgrade Hero Passive" is one more entry
  competing against DashSkill/HeroSkill/stat picks, swapping `HeroPassive` to the next-level asset —
  identical one-asset-per-level convention, not a special case.
- **Probably the first passive that actually needs a reactive hook** (`OnKill`/`OnDamageTaken`/
  `OnDamageDealt`) rather than pure `Tick()` — a hero-defining passive is much more likely to be "on
  kill, do X" or "on taking damage, do Y" than a generic timer pulse. The Autonomous-passives
  section above deliberately deferred those hooks until a concrete idea needed one; Hero Passive is
  probably that idea. Add the hook(s) when the first Hero Passive is actually built, not
  speculatively now.

### Candidate ideas (placeholders — no roster/character doc exists yet to pin these to)

Sketched to show the Level 0→5 growth shape, not as final content:

| Concept | L0 (baseline, always on) | Growth through L5 |
|---|---|---|
| **Momentum** — rewards sustained movement | Small move-speed ramp after running straight for a beat | + damage ramp alongside speed → higher cap → damage trail while at max ramp → dash charge refunded on reaching max → AoE burst on reaching max |
| **Bloodlust** — rewards kills | Tiny heal on kill | + brief attack-speed pulse on kill → bigger heal → pulse also boosts fire rate → kill chains extend pulse duration → kills also grant a small shield |
| **Vengeance** — rewards taking hits | Tiny thorns % reflect | + brief speed burst when hit → bigger thorns → thorns explodes in a small radius → brief invulnerability past a damage threshold → full retaliation nova |

Each row is a plausible `PassiveAbilityData` subclass (`MomentumPassiveData`, ...). Pick one (or
something else entirely) per character once actual heroes/roster exist — no `characters.md`/roster
doc exists in `Docs/` yet, so this is deliberately generic rather than tied to a named hero.

## Upgrade idea list

Grouped by mechanism, not theme — this determines which of the two shapes above a given idea needs.

### Stat deltas

| Idea | Field (on `CharacterStats`, unless noted) |
|---|---|
| Damage +5% | `DamageMultiplier` (applied centrally in `DamageUtility.ApplyDamage`, per the [earlier stats discussion](#) — see conversation/commit for the hook-in point) |
| Fire rate +10% | `FireRateMultiplier`, read by `WeaponSystem` |
| Move speed +8% | `MoveSpeedMultiplier`, read by `PlayerMovementProcessor`/KCC config |
| Max HP +15% | `MaxHealthMultiplier` |
| Health regen +1/s | `HealthRegenPerSecond` (needs a small regen tick somewhere — doesn't exist yet) |
| Armor +2 | flat add to `Health.Armor` |
| Shield capacity +10 / shield regen | `Health.CurrentShield`/new `ShieldRegenPerSecond` |
| Dash charge +1 | `SkillSlot.MaxStacks` (DashSkill) directly, not `CharacterStats` — see `skills.md` |
| Dash recharge speed +10% | multiplier applied to `SkillSlot.RechargeTimer` countdown rate |
| Pickup/magnet radius +20% | new field, read by whatever XP-orb/pickup attraction logic exists |
| XP gain +10% | multiplier on XP awarded per kill/pickup |
| Projectile speed/size +X% | read by `WeaponSystem`/`ProjectileSystem` when spawning |
| Crit chance / crit damage | new `CritChance`/`CritDamageMultiplier`, rolled in `DamageUtility` |

### Autonomous passives

| Idea | Behavior shape |
|---|---|
| Slow aura — enemies within radius move at X% speed | Continuous `Tick()`, no internal cooldown — re-applies a slow status every frame to enemies currently in range (needs an enemy-side "slow multiplier" field to apply to, doesn't exist yet) |
| Area damage — periodic AoE pulse centered on player | Periodic `Tick()` using its own `CooldownTimer`, fires via `ExplosionUtility.Explode` on trigger |
| Throw bombs randomly — lob a projectile at a random/nearest enemy every few seconds | Periodic `Tick()`, spawns via `ProjectileSpawner` targeting `EnemyMovementUtility`'s nearest-enemy query |
| Orbiting blades — projectile(s) that continuously circle the player and damage on contact | Continuous `Tick()` updating a spawned entity's orbit position; damage-on-contact likely needs its own trigger volume, not `DamageUtility.ApplyDamage`'s direct-hit path |
| Life on kill — heal X% max HP whenever you kill an enemy | First idea that actually needs a reactive hook (`OnKill`) rather than pure `Tick()` — add the hook when this is built, not before |
| Thorns — reflect X% of damage taken back at the attacker | Needs `OnDamageTaken`, same reasoning as life-on-kill |
| Freeze/stun chance on hit | Needs `OnDamageDealt`, same reasoning |

### Ability/weapon unlocks (existing systems, not new ones)

| Idea | Mechanism |
|---|---|
| Unlock second active skill | Assign an `AssetRef<SkillData>` to `CharacterSkills.HeroSkill`, currently unassigned — see `skills.md` Roster |
| Weapon level up | Swap `AssetRef<WeaponDataAsset>` on the equipped weapon, same "one asset per level" convention as `SkillData` |
| New weapon slot (dual-wield / second independent weapon) | Not designed yet — `Weapon.qtn`/`WeaponSystem` currently assume one active weapon per player; would need its own design pass, not covered here |

## Open questions

- Where does the level-up pick pool live, and how is it rolled? Likely a weighted
  `AssetRef<UpgradeData>[]` pool resolved via `f.RNG` for determinism, with already-maxed-level picks
  excluded from the roll — not designed yet.
- How does the player's choice get back into the simulation deterministically? Needs a Quantum
  command (input-adjacent), not a direct API call from the UI — not designed yet.
- `list` vs capped `array` for `PassiveSlot` storage — an unbounded `list` avoids picking an
  arbitrary cap but costs a heap allocation from the frame context; a capped array is simpler and
  matches `CharacterSkills`' existing fixed-slot style but requires guessing a max passive count
  upfront. Leaning `list` since a Survivors.io-style run can plausibly exceed any conservative cap.
- No roster/character doc exists yet — the Hero Passive candidate ideas above are placeholders
  until concrete heroes are defined; each hero also needs its own DashSkill/HeroSkill identity (only Dash
  exists today).
- Hero Passive's fixed-slot shape — a new field on `CharacterSkills` vs. a new sibling component —
  isn't decided.
- The pick pool needs to track *current level* for every leveled thing (DashSkill, HeroSkill, Hero
  Passive, and each individually-picked generic passive), so it can roll only the next level and
  stop offering something already at Level 5. Same underlying gap as "how is the roll resolved"
  above, just with four things needing it instead of one.
