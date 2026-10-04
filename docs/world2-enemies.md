# World 2 Enemy Roster (Desert / oil biome)

12 normal enemies in three folder groups (BlackMoles / DesertSecurity / Wildlife - organizational only; the old per-spawn `EnemyFaction`/`FactionSkins` skin system was removed, each enemy has one `ViewPrefab`). Every asset is a clone with its OWN Quantum guids (tuning never
touches World 1) under `Resources/Enemy/World2/<Group>/<Name>/W2-<Name>.asset`; placeholder views are
Prefab Variants of the W1 view under `_Project/Prefabs/View/EnemyView/World2/<Group>/`. None are in a
`SurvivalConfig`/group yet - spawn with `EnemyDataAsset`'s "Spawn Near Local Player" Inspector button.
Naming: W1 enemy files are `W1-*`, W2 are `W2-*`; `_Hazards/` and `_Passives/` are shared (no prefix).

| Enemy | Group | Tier | Built from | Notes |
|---|---|---|---|---|
| Mole Grunt | BlackMoles | Filler | W1-Filler | own copy of the swarm melee action (W1-Filler borrows W1-Swarm's) |
| Fuel Runner | BlackMoles | Filler | W1-Suicider | `OilTrail` passive (small puddles while running) + `FuelRunnerOilSplashSpawn` on its blast (StickyPuddle Scale 1.5, 4s) - see `docs/enemy-passives.md` |
| Mole Sniper | BlackMoles | Normal | W1-SniperEnemy | |
| Mole Rusher | BlackMoles | Specialist | W1-Charger | |
| Mole Enforcer | BlackMoles | Heavy | W1-HeavySlammer | |
| Security Bot | DesertSecurity | Normal | W1-Gunner | own copy of the ranged projectile |
| Tar Launcher | DesertSecurity | Specialist | W1-MortarEnemy | sticky tar puddle - see `docs/mortar-elite.md` |
| Shield Bot | DesertSecurity | Specialist | W1-Shielder | |
| Scarab Swarm | Wildlife | Filler | W1-Swarm | |
| Sandworm Larva | Wildlife | Normal | W1-NormalMelee + elite burrow | see `docs/enemy-burrow.md` |
| Scarab Nest | Wildlife | Specialist | W1-Turret (stationary) | `SpawnPackDeliveryData` hatching Scarab Swarms |
| Dune Crusher | Wildlife | Heavy | W1-HeavySlammer body | W1 boss cone punch, retuned |

## Survival configs (Director)
- **`Director/World1/W1-SurvivalConfig.asset`** - the live Grassland Outpost run (formerly
  `SurvivalWorld1Config_Iteration3`, same guid, so scenes/RuntimeConfig still point at it) + its 5 packs in
  `World1/Groups/W1-*`. Generator: Tools/RiftRaiders/Generate Survival World 1 Content (W1-SurvivalConfig) -
  re-running it overwrites hand edits.
- **`Director/World2/W2-SurvivalConfig.asset`** - World 2's own run, authored by
  `Editor/SurvivalWorld2ContentGenerator.cs` (Tools/RiftRaiders/Generate Survival World 2 Content) - edit
  the generator, not the asset (re-running overwrites). Same skeleton as W1 (4 Runs x 7 segments, Breathing
  60/60/60/90, Boss last); own curriculum, one new mechanic per teaching segment:
  Run 1 Desert Arrival (base reskins + **Sandworm Larva**), Run 2 Desert Security (**Shield Bot**,
  **Tar Launcher**), Run 3 Mole Arsenal (**Mole Sniper**, **Fuel Runner**, Enforcer via pack), Run 4 The
  Dunes Wake (**Dune Crusher**, **Scarab Nest**, Final Exam). Pacing opens near W1's Run 1 end-state and the
  Final Exam is ~15% over W1's (budget 34 vs 30, max alive 32 vs 28). 6 packs in `World2/Groups/`:
  ScarabRush, ShieldGunline, EnforcerScarab, TarGrunt, SniperShield, CrusherLarva. Tier C (Rusher, Enforcer,
  Tar Launcher, Fuel Runner, Dune Crusher, Scarab Nest) is capped and loose-OR-packed per phase. **Elites (4)
  are World 1's placeholders; the Boss is the Dune Leviathan** (see below).
  Not wired to any scene/RuntimeConfig yet; not playtested.
- Older drafts (`SurvivalConfig`, `_MVP`, `SurvivalWorld1Config`, `_Iteration2`, `TestConfig`) and their 78
  groups in `Director/EnemyGroups/` are legacy - nothing in the game references them.

## Boss - Dune Leviathan
Burrowing sandworm that final-exams World 2's language (Larva burrow, Crusher cone, Tar puddles, Fuel
Runner oil, Nest brood). `Enemy/World2/Boss/DuneLeviathan/W2-DuneLeviathan.asset` (BossDataAsset, cloned
from W1-World1Boss for structure/stagger) + entity `Entities/Enemies/W2-DuneLeviathanPrefab` (copy of
World1BossPrefab - **W1 visual is a placeholder**). Wired as the Boss phase of W2-SurvivalConfig by the
W2 generator.
- **Basic - LeviathanBite**: wide cone slam (Dune Crusher's, 120°, range 5, 30 dmg, 1s windup, 1s recovery).
- **Skill 0 - LeviathanHunt**: homing burrow (`DirectionTracking` WhileActive) at 7 u/s, max 3s underground,
  erupts under the target (`AttackOnResurface`, radius 3, 25 dmg + knockback) after a 0.7s ground warning.
- **Skill 1 - LeviathanTarRain**: Elite-Mortar barrage, 7 shells (2 aimed) of its own tar shell that leaves a
  StickyPuddle (Scale 1.3).
- **Skill 2 - LeviathanBrood**: SpawnPack of Sandworm Larva + 2 Scarab Swarm (3/4/5/6 by players), 14s cd.
- **Skill 3 - LeviathanFrenzy**: faster homing burrow (9 u/s, 2s max, 0.6s warning) - the ComboChain trigger:
  x3 with retarget each, then **Exposed 3s at 1.5x damage taken** (Rupture).
- **Skill 4 - Kneel** (shared) - stagger break only.
- **Passive LeviathanOilSeep**: GroundTrail **BurrowedOnly** + `OnlyDuringAction = LeviathanFrenzy` - oil seeps up only along Frenzy tunnels (phase 3), so the plain Hunt stays clean.
- **Phases** (by HP): >80% Bite + Hunt; 80-40% + Tar Rain (dmg 1.2x); <40% Tar Rain + Brood + Frenzy (dmg 1.4x).
Not playtested; numbers are first-pass.

## Dune Crusher - boss-style cone slam
`DuneCrusherConeSlam` / `...Delivery` / `...Telegraph` are copies of `W1-World1Boss-Punch`,
`-GroundPunchDelivery`, `-ConePunchTelegraph` (GroundAreaDeliveryData, ConeShaped, live-tracking cone
telegraph). Retuned for a Heavy: cone 100° (boss 180), range 3.5 (boss 6), damage 30 (boss 25), windup
1.0s (boss 0.7), DownTime 1.2s, cooldown 3s; boss-only anticipation body sprite removed. Keeps
GenericDamageX1 + KnockbackStrong.

## Scarab Nest - spawner
Stationary and immovable: `Stats.Movement` = its own `ScarabNestStationaryMovement`
(`StationaryMovementData` - zero direction, `IsKinematic` true), so other enemies/players can't push it
and knockback doesn't move it (see "Kinematic movement" below). `ScarabNestSpawnAction` (Origin Self, EngageRange 12, windup 0.8s,
cooldown 6s, no damage) → `ScarabNestSpawnDelivery` (`SpawnPackDeliveryData`, Composition =
[W2-ScarabSwarm], MaxEnemies 3/4/5/6 by player count, 1-2.5 around the nest). Caveat inherited from
SpawnPack: hatched swarms get no `EnemyLifecycle`, so they don't count toward the Director's alive cap or
pressure - a nest left alive keeps adding swarms every 6s with no ceiling. Add a per-nest cap before
putting it in a SurvivalConfig.

## Once-per-blast hazards on GroundAreaDeliveryData (2026-10-02)
`GroundAreaDeliveryData` (slams, Suicider-style self-destruct) now applies `AppliesOncePerBlast` effects
(e.g. `SpawnEntityEffectData`) once at the blast origin via the now-public
`HitEffectUtility.ApplyBlastLevelEffects` - both in `Begin()` and in `DetonateAtCorpse` (killed by damage),
even when it catches nobody. Before, per-target `ApplyToTarget` ran them once per player caught at that
player's feet (and DetonateAtCorpse skipped them entirely). No existing action used a spawn effect on this
delivery, so nothing else changed.

## Kinematic movement (`EnemyMovementData.IsKinematic`)
Generic, reusable by any enemy (turrets, totems, an "immovable" elite later). A movement asset whose
`IsKinematic` is true (base default false; `StationaryMovementData` overrides it) makes
`EnemySystem.SeedAnchored` tag the enemy `Anchored` at spawn. `AnchoredSystem` (registered right before
`DestroyAfterTimeSystem`, after every system that can touch the body) re-forces `IsKinematic = true` and
zeroes velocity every tick - the enemy state machine flips IsKinematic off in ~10 places (recovery,
knockback recovery, stagger, root, Juggernaut push), and re-pinning once at the end of the tick covers
them all without special-casing each. On its first tick with ground under it, `AnchoredSystem` also lifts
the enemy to ground + collider half-height (`Anchored.Placed`): spawners put the pivot AT ground height and
rely on physics depenetration to push a dynamic body up, which a kinematic body never gets - it stayed
half-buried. Done in the system (not at spawn) so every spawn path is covered.

## Known leftovers
Every enemy cloned from a W1 base carries 3 hidden legacy root fields (`Movement`, `Targeting`,
`BasicAction` on `EnemyDataAsset`, `[HideInInspector]`) pointing at guids that never existed - present
on the W1 originals too. The live fields are `Stats.Movement`, `AI.Targeting`, `Actions.BasicAction`.

## Boss physics (2026-10-03)
Both boss prefabs (`World1BossPrefab`, `W2-DuneLeviathanPrefab`) use PhysicsBody Mass **10000** (was 1, while
`GenericEnemyPrefab` is 100) so Director enemies can't shove the boss. Knockback is mass-independent
(`DamageUtility` scales the impulse by the target's own mass) and enemy movement writes velocity directly,
so mass only changes collision pushing.
