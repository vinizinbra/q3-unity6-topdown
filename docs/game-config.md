# Game Config (OverallGameConfig vs RuntimeConfig)

Configs come in two kinds, and they now live in two places.

| | **`OverallGameConfig`** (one asset) | **`RuntimeConfig`** (per match / per scene) |
|---|---|---|
| What | Shared tuning that's the same for every match and barely changes | What this match plays |
| Where | `Resources/Configs/OverallGameConfig.asset` | MenuScene's `MatchMakingConfig`, plus each gameplay scene's `QuantumDebugRunner` |
| Holds | ExplodeOnDeath, Effect, ElementalReaction, EnemyTierResistance, EnemyTierStats, Balance, Difficulty (the table), Director, Lifecycle, Experience, LevelUp, Scrap, RiftShard, Coin, HealthOrb, CursedRift, Store, Blacksmith, TraversalChallenge (timer per difficulty), Talents, AccessoryGuard, Revive, `Prefabs` (orb/accessory prototypes) | `GameConfig` (→ the asset), Map/Seed, `LevelConfig` (normal vs Training Zone), `WorldDefinition` (+ derived hidden `SurvivalConfig`/`WorldBalance`), `Difficulty`/`NightmareLevel` (the pick), `WorldUnlocks`, `Bots`, the `Debug*` knobs |
| Read in the sim as | `f.GameConfig.StoreConfig` (`Frame.User.cs` resolves `RuntimeConfig.GameConfig`) | `f.RuntimeConfig.WorldDefinition` |

**Why:** before this, every `RuntimeConfig` (menu + one per debug-runner scene) carried its own copy of ~25 asset
references. They were identical, but nothing kept them that way. Now there's one asset to edit, and every scene
points at it.

## Adding a new shared config
1. Create the asset in `Resources/Configs/`.
2. Add an `AssetRef<YourConfig>` field to `OverallGameConfig` (`Simulation/Assets/Config/OverallGameConfig.cs`),
   with a comment saying what reads it.
3. Assign it on `OverallGameConfig.asset` (once).
4. Read it as `f.FindAsset(f.GameConfig.YourConfig)`.

Only put something on `RuntimeConfig` if it really can differ match to match (a menu pick, a per-scene level,
a debug knob).

## Folder
Every shared config asset is in `Assets/_QuantumUser/Resources/Configs/`. These were moved in on 2026-10-20 (GUIDs kept):
DirectorConfig, LifecycleConfig (were `Director/`), ScrapConfig (was in Capy's skill folder), HealthOrbConfig
(`Resources/` root), CursedRiftConfig (`Sacrifice/`), StoreConfig, BlacksmithConfig (`Store/`), TalentsConfig
(`Talents/`), AccessoryGuardConfig (`Accessory/`), ReviveConfig (`Revive/`). Their content generators write to the
new paths. Per-world assets (SurvivalConfigs, WorldBalance, WorldDefinitions) stay under `Director/World*` and `Worlds/`.

## Gotchas
- `f.GameConfig` is null if a `RuntimeConfig` has no `GameConfig` assigned, and every shared lookup then throws. All three
  current RuntimeConfigs (MenuScene, GrasslandOutpostGameScene, TrainingZoneScene) are assigned. A new debug-runner scene
  must assign it too.
- Debug-runner scenes: `DebugRunnerWorldSync` (Editor) re-derives the hidden `SurvivalConfig`/`WorldBalance` from
  `WorldDefinition` on every Play, so the world you pick on the runner is the world you get (see `docs/worlds.md`).
