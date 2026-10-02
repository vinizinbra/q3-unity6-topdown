# Enemy Passives (+ Ground Trail, Sticky Slow hazards)

Reusable, data-carrying enemy behaviours that aren't attacks - an oil trail today; auras, on-death
spawns, etc. later. **Long-run goal:** Elites get a randomly rolled pack of passives (and traits) at
spawn, so every passive is written once as an asset and works on any enemy, Normal or Elite.

## Pieces

- **`EnemyPassiveData`** (`Simulation/Assets/Enemy/Passives/`) - abstract `AssetObject` with
  `Apply(Frame, EntityRef enemy)`. A passive usually just adds/configures a component that its own
  system drives. Distinct from `EnemyStatsData.Traits` (bare enum flags, no data).
- **`EnemyDataAsset.Passives`** - authored list, applied once at spawn by
  `EnemySystem.SeedFromEnemyData` → `SeedPassives`.
- **`EnemyPassiveUtility.Apply(f, enemy, passiveRef)`** - the single entry point. Records the
  passive on **`EnemyPassives`** (`QTN/Enemy/EnemyPassives.qtn`, fixed array of 8, added on demand)
  then calls `Apply`. Refuses duplicates. The future Elite roller calls this same method, and the View
  can read `EnemyPassives` to show an elite's affixes.
- **`GroundTrailPassiveData`** → **`GroundTrail`** component (`QTN/GroundTrail.qtn`, generic - not
  enemy-only) + **`GroundTrailSystem`** (registered after `AreaDamageSystem`). Drops `Prototype` via
  `SpawnedEntitySpawner` every `SpawnDistance` of flat travel, each living `Duration`. Pauses while
  `Burrowed` or dead. Live pieces ≈ speed / SpawnDistance × Duration - keep it modest.
- **`StickySlowEffectData`** - ground-hazard slow on its own `StatusEffects.StickySlow*` slot (not
  Ice: no Freeze/reactions; not TempMoveSpeed: can't clobber an Energy Drink). Strongest-wins, no
  stacking, `OnlyWhenGrounded` (KCC) by default. Read in `PlayerMovementProcessor`.

## Assets (`Resources/Enemy/`)

- `_Passives/OilTrail.asset` - GroundTrail → StickyPuddle at Scale 0.75, SpawnDistance 1.5, Duration 2.5.
- `_Hazards/StickyPuddle/` - the ONE sticky puddle prefab, authored at radius 1 (same idea as
  `SimpleExplosion` being authored for radius 1): AreaDamage tick 0.2, Damage 0, TargetMask Players,
  Effects = [`StickySlow`], `GroundOffset.Offset = -1` (sphere centre on the ground), particle visual
  (below). Callers
  pick the size with a per-spawn **Scale** - `SpawnEntityEffectData.Scale` (Tar Launcher 1.5) or
  `GroundTrailPassiveData.Scale` (Oil Trail 0.75) - through `SpawnedEntitySpawner.Spawn(scale:)` →
  `ApplySpawnScale`, which scales the collider AND `GroundOffset.Offset` together.
- Puddle visual = particles only (no mesh), via **`AreaParticleEmitterView`** (generic - any spawned
  area can use it) with one entry per layer, each authored for a radius-1 area:
  `OilSplash.prefab` (one burst of 5 big black circles evenly spread on a 0.55 ring - Shape arc mode BurstSpread, random yaw per spawn - so they always cover centre and edge; ScaleSizeWithRadius) and
  `OilBubbles.prefab` (one burst of 10 per radius-1 area on an edge ring; ScaleEmissionWithArea, which
  scales rate AND burst counts by radius²). Both use **MatchEntityLifetime**: start lifetime = the
  hazard's DestroyAfterTime ± `LifetimeJitter` (0.5s), and Size over Lifetime is rebuilt to hold full
  size until the last `ShrinkDuration` (0.5s), after a `GrowInDuration` (0.2s) pop-in from 0. HorizontalBillboard, `_Project/Art/Material/ParticleAlphaCircle.mat`, sorting order Splash -2 / Bubbles -1 (Burning Ground uses -1).
  The instance is held stopped until the entity settles; its StopAction is only switched to Destroy
  in Release (a stopped system with no particles destroys itself at once - that killed every trail
  drop, which spawns above the ground and settles a few ticks later). Instantiated per spawn; shape radius / size / rate read back from the actual collider; on despawn
  each is detached and only stops emitting - live particles finish, then `StopAction.Destroy`.
- `_Hazards/StickySlow.asset` - shared slow (0.65 speed, 0.25s).
- `World2/BlackMoles/FuelRunner/W2-FuelRunner.asset` - cloned Suicider chain (own guids) +
  `Passives = [OilTrail]`.
- `World2/DesertSecurity/TarLauncher/` - see `docs/mortar-elite.md`.

## Current status / next

Compiles; not yet verified in Play Mode. Neither enemy is in a SurvivalConfig/group - spawn with the
`EnemyDataAsset` "Spawn Near Local Player" Inspector button. Not done: puddle visuals (placeholders),
a global cap on live puddles, the Fuel Runner's death blast leaving a puddle, and the Elite random
passive-pack roller (weighted pool asset, incompatibility tags, count by tier/difficulty, elite name/
icons in the View).
