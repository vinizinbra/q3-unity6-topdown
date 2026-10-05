# Worlds (world selection: look + Director + per-world balance)

A "world" bundles what a match looks like and how it plays, so the same gameplay scene serves every
world and adding one is just authoring assets.

## Pieces
- **`WorldDefinition`** (`View/World/WorldDefinition.cs`, plain ScriptableObject) - DisplayName, Icon,
  `Theme` (WorldTheme - sky/tileset/water/blood, view-only), `SurvivalConfig` (the world's Director
  timeline), `Balance` (optional `WorldBalanceData`). `ApplyTo(RuntimeConfig)` writes
  `WorldDefinition`, `SurvivalConfig`, `WorldBalance`.
- **`WorldCatalog`** (`View/World/WorldCatalog.cs`) at **`Resources/Worlds/WorldCatalog`** - ordered list;
  the index (0 = World 1) is the world's id.
- **`RuntimeConfig.WorldDefinition`** + derived **`WorldBalance`** (`AssetRef<WorldBalanceData>`) -
  serialized with the rest of RuntimeConfig, so every client (incl. reconnects) knows the world.
- **`WorldBalanceData`** (`Simulation/Balance/`, AssetObject) - per-world multipliers on top of the GLOBAL
  `BalanceConfig` (which keeps the run curves and every co-op table). Channels and consumers:
  EnemyHp/EnemyDamage (`EnemyBalanceUtility.ResolveEnemyStats`), DirectorBudget
  (`CombatDirectorUtility.ResolveBudgetMultiplier`), DirectorPressure
  (`PlayerClusterDirectorUtility.ResolveCoopPressure` - TargetPressure, MaxAlive, purchases), XpRequirement
  (`ExperienceUtility.ResolveXpRequirementMultiplier`), CoinGain (`CoinUtility.ResolveCoopCoinGain`).
  Unassigned = all 1x.

## Authoring: one slot
Fill only **`RuntimeConfig.WorldDefinition`** (MatchMakingConfig's RuntimeConfig in MenuScene) with e.g.
`W1-GrasslandOutpost` - it must also be listed on `WorldCatalog`. `WorldDefinition` is a Quantum `AssetObject`
(Theme is view-only, the sim never reads it). `RuntimeConfig.SurvivalConfig`/`WorldBalance` are now hidden,
**derived** fields: `MatchMakingConfig.Awake` and `ApplyWorld` (menu picker / party sync) fill them via
`WorldDefinition.ApplyTo` before CloneConfig. `EnvironmentManager` reads the Theme straight from
`RuntimeConfig.WorldDefinition`. The old `RuntimeConfig.World` int is gone; the party still syncs the catalog index.

## Flow
1. Menu (`MainMenuWindow.worldDropdown`, MenuScene `PlayArea/WorldDropdown`) lists the catalog, remembers
   the pick (`menu_world_index` pref) and calls `MatchMakingConfig.ApplyWorld` on the RuntimeConfig.
   Only the party leader can change it (same gating as difficulty).
2. Party: the leader sends `"world"` (index) in `SyncMatchRoom` next to difficulty; every client re-applies
   that index from its own catalog.
3. Gameplay scene: `EnvironmentManager` loads `WorldCatalog[RuntimeConfig.World].Theme` (at Awake if the
   game already runs, else on `CallbackGameStarted`). Its `initialTheme` is only the editor/preview fallback.

## Authored
| Index | Asset (`Resources/Worlds/`) | Theme | SurvivalConfig | Balance |
|---|---|---|---|---|
| 0 | W1-GrasslandOutpost | GrasslandOutpostTheme | W1-SurvivalConfig | W1-WorldBalance (all 1x) |
| 1 | W2-DesertSky | DesertSkyTheme | W2-SurvivalConfig | W2-WorldBalance (HP 1.15, Dmg 1.1) |

## Not done / caveats
- Not playtested end-to-end (menu -> match) yet.
- The Balance Simulator reads `BalanceConfig` only - it doesn't apply `WorldBalanceData` yet.
- LevelConfig (chunk pool / layout) is still shared; add it to `WorldDefinition` when worlds need their own.
