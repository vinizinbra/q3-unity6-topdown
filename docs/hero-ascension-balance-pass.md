# Hero Ascension Balance Pass — plan, decisions, status

Working doc for the 2026-08-20 pass that normalizes Max / Pixie / Kai / Brute / Zara / Lux to
**9 Ascension lines × 3 ranks**, rebalances the existing ones, and fully refactors Zara and Lux.

The brief itself is the specification; this doc records the **architecture decisions, deviations and
progress**, which the brief can't. Per-hero design detail still lives in each hero's own doc
(`docs/max-ascensions.md`, `docs/pixie-ascensions.md`, …).

---

## Order of work

1. Inspect existing ranked-Ascension architecture — **done**
2. Generic primitives (everything else depends on them) — **done**
3. Max targeted changes — **done**
4. Pixie targeted changes — **done**
5. Kai targeted changes — **done**
6. Brute targeted changes — **done**
7. Zara full refactor — **done**
8. Lux baseline + full refactor — **done**
9. Editor asset generators for all six heroes — **done**
10. Per-hero doc updates — **done**

## Verification

All four assemblies were compiled offline (`dotnet build` against patched copies of Unity's own
generated `.csproj` files, since the checked-in ones carry a stale source list) after the Editor's
own codegen had regenerated from the new `.qtn` set:

| Assembly | Result |
|---|---|
| `Quantum.Simulation` | **0 errors** |
| `Quantum.Unity` (View) | **0 errors** |
| `Quantum.Unity.Editor` (all six asset generators) | **0 errors** |
| `Assembly-CSharp` | **0 own errors** |

Nothing has been run or verified **in-Editor** yet: no asset generator has been executed, so the live
`.asset` files still describe the old rosters.

---

## Generic primitives created or extended

Everything below is hero-agnostic by construction; heroes are consumers, never special cases.

| Primitive | What it is | First consumers |
|---|---|---|
| **Hard-CC diminishing returns** | `EnemyTierResistanceConfig.StunImmunityDuration`/`InterruptImmunityDuration`/`ImmuneToHardCC` + `StatusEffects.StunImmunityRemaining`/`InterruptImmunityRemaining`. `ApplyStun`/`TryInterrupt` reject rather than refresh while the window runs. Defaults: Filler/Normal none, Specialist 2s, Heavy 3s, Elite 4s, Boss immune. | Kai Singularity gravity pulses, Brute Concussive Impact, Zara Bass Drop |
| **`WallSlamUtility`** | The shared *knockback source → enemy movement → valid wall impact → wall-slam effect* step. Extracted verbatim from Iron Shoulder's own private version; reports the wall hit **and**, separately, whether the Stun genuinely landed. Also owns the presentation half — it raises the generic `WallSlammed` event itself, so any knockback source added later gets the shared wall-impact VFX/shake with no hookup. | Brute Iron Shoulder, Brute Groundbreaker |
| **`LandingSource` + a 3-arg `OnPlayerLanded`** | The pre-existing generic landing signal now also carries *why* the player was airborne (`Fall`/`Jump`/`Launched`), tracked on `PlayerMovement.AirborneSource` and claimed by `AutoJumpSystem.DoJump` / `DamageUtility.ApplyResolvedImpulse`. | Brute Groundbreaker |
| **One shared aura-DR slot** | `GuardianDamageReduction*` renamed → `AuraDamageReduction*`, now **take-the-stronger**. Two aura sources never stack additively; strongest wins. | Brute Guardian, Lux Fire Support |
| **`AreaAllyBudget` + `AreaAllyBudgetUtility`** | Per-**spawned-deployable**, per-ally spend caps (HP healed, cooldown reduced). Lives on the area entity, so a fresh deploy = a fresh allowance and two Zaras never share one. | Zara Totem healing cap + Sound Boost cooldown cap |
| **`ModifyRemainingCooldownEffectData`** | Generic "reduce this ally's remaining skill cooldown" hit effect, budget-aware. Clamped at 0, never banks. | Zara Sound Boost R2 |
| **`AllyBuffEffectData`** | Generic bundle: Move Speed / Fire Rate / outgoing damage / DR / flat Shield, all opt-in. | Zara Support Beat + Portable Speaker, Lux Fire Support |
| **`DelayedBlast` + `DelayedBlastSystem`** | Generic one-shot "go off shortly, over there" blast parked on the owner. | Pixie Unstable Mixture R3, Brute Aftershock R3 |
| **`DespawnIntent` + `DespawnIntentUtility`** | Despawn/death **reason tags** so housekeeping removals don't fire on-death effects. | Lux Sentry replace/relocate, Zara Speaker replace |
| **`StatusEffects.TempOutgoingDamage*`** | Timed outgoing-damage buff across every `DamageSource` (the Weapon-only pair already existed). | Zara Power Chord |
| **`HitEffectContext.SourceEntity`** | The area entity that produced a hit, so a per-instance effect can find its own instance. | `AreaAllyBudget` consumers |
| **`WeaponSystem.RefillMagazine`** | Explicit one-shot magazine refill, event-driven instead of a live condition. | Max Full Throttle R3 |
| **`ExplosiveSequenceChance`/`Cooldown`** | Optional proc chance + optional internal cooldown on the shared explosive-proc path. Both default to "off", so the Explosive Sequence perk is unchanged. | Pixie Explosive Rounds |
| **Proc-source tagging (reused, not new)** | `isExplosion` + `isChainedExplosion` now also gate Unstable Mixture's stack gain *and* spend — a chained blast is a payout, never a new link. | Pixie |

---

## Deviations from the brief (deliberate, with reasons)

1. **Pixie's pool split is 3 Hero Skill / 3 Passive / 3 Dash, not 4/3/2.**
   The brief groups Hot Fuse under "Bunny Bomb" but its mechanic is *"Dash empowers the next Bunny
   Bomb"*. A Hero-Skill-pool `SkillActionData` only executes when the Hero Skill is cast, which is
   too late to empower that same throw — and moving it there would have introduced a bootstrap gap
   (pick it, dash, nothing happens until you first cast Bunny Bomb). Kept it in the Dash pool.
   The hard target (9 lines × 3 ranks = 27) is met; only the preferred pool distribution differs.
   Direct Hit *did* move into the Hero Skill pool as specified.

2. **Uncontrolled Fury R3's Vendetta bonus is capped, not uncapped.**
   The brief says "+2s instead of +1s" for Vendetta kills *and* "there must be a hard configurable
   maximum extension per activation". The pre-existing code had the Vendetta bonus explicitly
   uncapped. Every extension source now books against one shared per-activation ledger
   (`OverdriveExtension`), including Vendetta Strike R3's — no combination can produce a permanent
   Overdrive.

3. **Zara's Portable Speaker inherits Sound Boost through authored reduced-effect assets**, not a
   runtime 50% multiplier. The brief asks for "a different data profile, not complex hero-specific
   inheritance code" — so each Sound Boost rank authors both a full and a Speaker-variant buff asset.
   Double Time / Main Stage / Amplifier *are* inherited via the runtime fraction, since those are
   plain numbers.

4. **Max's Wildfire is not prerequisite-gated on a Burn source**, unlike Flashpoint. Wildfire spreads
   *existing* Burn (which a weapon's elemental proc can supply), and gating two of nine lines behind
   one Overdrive/Dash pick narrowed the draft pool too far.

5. **Kai's per-vortex interrupt tracker was deleted, not kept alongside** the new generic CC immunity.
   It only ever protected against one vortex; the generic window also covers a second Singularity,
   a Brute stun, and anything added later.

---

## Balance-sensitive values left configurable for playtesting

Every number in the brief is exposed on its own asset. The ones most likely to need a first pass:

- Max: `BerserkSkillData.BaseMaxExtension` (baseline Overdrive extension ceiling), Last Stand
  `RageLossFraction`, Blood Debt `MaxHealFractionPerKill`, Cremation's three thresholds.
- Pixie: Explosive Rounds `ProcChance` **and** its optional `ProcCooldown` (shipped off), Unstable
  Mixture per-stack bonuses + secondary blast, Blast Jump `TriggerRadius`.
- Kai: `EnemyTierResistanceConfig` interrupt windows (these govern Singularity's whole feel),
  Event Horizon's projectile-speed values.
- Brute: Aftershock stack values + Earthquake, Guardian's reactive DR trio, Bodyguard's flat Shield
  and per-ally cooldown, Groundbreaker's `MinimumFallHeight` (the single value deciding whether the
  line ever triggers on real terrain) and `WallCheckDistance`.
- Zara: `Resonance.Max` vs `GenerationPerDamage` (tune together against a ~10-12s pulse cadence),
  `MaxHealFractionPerAlly` (global Totem healing cap), `MaxCooldownReductionPerTotem` (shipped
  generous at 6s; expected tuning range 3-4s), `MaxActiveSpeakers`.
- Lux: Scrap drop chances, `StacksRequired`, max active Sentries, Field Modification stack values,
  Redline threshold, lifetime-extension budget.

---

## Follow-up: Brute's third Passive replaced (same day)

After the pass above landed, **Unstoppable** was cut and **Groundbreaker** put in its place — Brute
stays at 9×3, only his third Protector line changes. Full writeup in `docs/brute-ascensions.md`.

Unstoppable's CC-resistance/immunity/Momentum-on-hit design overlapped Momentum's own space. Two
generic hooks had been created **solely** for it and had no other consumer, so both were removed with
it rather than left as invisible dead code:

- `CharacterStats.HardCcDurationMultiplier` (+ `StatusEffectUtility.GetHardCcDurationMultiplier`, +
  its `CharacterSystem` seed) — Unstoppable R1's only mechanism.
- `StatusEffects.HardCcImmunityRemaining` (+ its tick, + its checks in `ApplyStun`/`ApplyRoot`/
  `TryConsumeInterruptImmunity`) — Unstoppable R3's only mechanism.

The per-tier **hard-CC diminishing returns** row above was deliberately **kept** — Kai's Singularity,
Brute's own Concussive Impact and Zara's Bass Drop all rely on it.

Two primitives were added in exchange (both already in the table above): `WallSlamUtility`, which
makes wall slams one shared implementation across Iron Shoulder and Groundbreaker rather than two, and
`LandingSource`, which finally gives the long-dormant generic `OnPlayerLanded` signal a consumer.

---

## 2026-08-20 — Skill Area audit: every hero's skill areas now scale

`StatUtility.GetAreaMultiplier` (the generic **Skill Area** global upgrade,
`CharacterStats.AreaRadiusMultiplier`) reached most areas through shared funnels but missed a scattered
set of hero-specific radii that each computed their own. Audited every skill radius in the game and
filled the gaps.

### Already covered (unchanged) — the shared funnels

| Funnel | Covers |
|---|---|
| `AreaHitData.Detonate` | **Pixie's Bunny Bomb**, cluster bomblets, weapon explosions, every `AreaHitData` blast |
| `ExplodeOnDestroyUtility.TryDetonate` | dropped/planted bombs (Backblast, Pocket Bombs, DashBomb) |
| `SpawnVortexEffectData` | **Kai's Vortex** — and through its collider, Compression / Vortex Collapse / Void Shards, which all derive from it |
| `HitPathSkillAction` / `SpawnEntitySkillAction` | every generic path/spawn skill action |
| `JuggernautSkillData` | **Brute's Discharge** (`KnockbackRadius`) and **Aftershock** (incl. its stack bonus) |
| `ResonanceUtility.FirePulse` | **Zara's Resonance pulse** |
| `DirectHitData` / `WeaponSystem` | weapon-perk explosive procs |

### Gaps filled

| Hero | Area | Where |
|---|---|---|
| **Lux** | Sentry **Range** — targeting, Fortification aura and the range ring all read it | `SpawnSentrySkillAction` |
| **Zara** | **Totem** (the deployed area's collider) | `SpawnAlternatingAreaEffectData.ApplyAreaMultiplier` |
| **Zara** | Portable Speaker radius | `PortableSpeakerSkillAction` |
| **Zara** | Afterbeat start/end pulses | `AfterbeatSkillAction` |
| **Brute** | Concussive Impact landing shockwave | `JuggernautLandingImpactSystem` |
| **Brute** | Iron Shoulder rank 3 wall shockwave | `IronShoulderSkillAction` |
| **Brute** | Groundbreaker landing shockwave | `BruteGroundbreakerSystem` |
| **Kai** | Mirror Step reflect radius | `MirrorStepSkillAction` |
| **Max** | Ignition's **Inferno** burst | `MaxAscensionUtility` |
| **Max** | Ignition's **Burning Ground** patch | `MaxAscensionUtility` |
| **Max** | Vendetta Strike dash sweep | `VendettaStrikeSkillAction` |
| **Pixie** | Blast Jump's dash-detonate trigger radius | `BlastJumpSkillAction` |

**Max is not exempt.** His kit is mostly self-buff (Overdrive's fire rate / damage / move speed /
reload, Vendetta's marks), but Ignition's Inferno burst and Burning Ground patch and Vendetta Strike's
dash sweep are genuine skill areas and now scale like everyone else's.

### Deliberately NOT changed — these would double-apply

Values **derived from an already-scaled radius** must not be scaled again:

- Main Stage's `RadiusBonus` — multiplies the Totem's collider, which is now already scaled.
- Compression's `ImplosionRadiusFraction`, Vortex Collapse's `RadiusMultiplier`, Void Shards' search —
  all fractions/multipliers of the vortex collider, already scaled at spawn.
- Aftershock's `EarthquakeRadiusMultiplier` — multiplies the already-scaled Aftershock radius.
- Direct Hit's `InnerRadiusFraction`, Unstable Mixture's per-stack radius bonus — fractions of an
  already-scaled blast.
- Hot Fuse / Blast Jump's bomb `RadiusMultiplier` — composed inside `AreaHitData.Detonate`, which
  already applies Skill Area.

Also left alone on purpose: **utility ranges that aren't areas** — Bodyguard's ally search, Emergency
Repair's `Range`, Relocation Protocol's `PickupRange`, Blast Jump's `Window`. These are "how far can I
reach", not "how big is my effect"; scaling them with an area stat would be a different design decision.

### Note on centralizing

Folding the multiplier into `SpawnedEntitySpawner.ApplyRadiusUpgrade` (the one funnel every deployable
passes through) was considered and rejected: `SpawnEntitySkillAction.ApplyScale` and
`SpawnVortexEffectData` both already multiply the collider themselves and would have double-applied,
and the spawner has no `SkillSlot` to fold `slot->AreaMultiplier` in with. Per-site remains the
codebase's existing idiom.
