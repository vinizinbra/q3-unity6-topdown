# World 2 Enemy Roster (Desert / oil biome)

12 normal enemies in three factions. Every asset is a clone with its OWN Quantum guids (tuning never
touches World 1) under `Resources/Enemy/World2/<Faction>/<Name>/W2-<Name>.asset`; placeholder views are
Prefab Variants of the W1 view under `_Project/Prefabs/View/EnemyView/World2/<Faction>/`. None are in a
`SurvivalConfig`/group yet - spawn with `EnemyDataAsset`'s "Spawn Near Local Player" Inspector button.
Naming: W1 enemy files are `W1-*`, W2 are `W2-*`; `_Hazards/` and `_Passives/` are shared (no prefix).

| Enemy | Faction | Tier | Built from | Notes |
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
