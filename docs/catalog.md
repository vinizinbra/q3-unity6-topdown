# Catalog (menu)

Menu tab listing everything the player can run into: **Enemies**, **Bosses**, **Rift Mutations**. Tiles are grouped into
sections, and the selected entry's details show on the right.

## Rule: the game's own data, never a second list
There are no per-entry Catalog assets. `CatalogTab` builds one runtime `CatalogEntry` per thing the game can actually
produce:

| Page | Source | Section | Subtitle |
|---|---|---|---|
| Enemies | every `WorldDefinition.SurvivalConfig` phase: `AllowedEnemies`, `AllowedGroups`/`GuaranteedGroup` members, `GuaranteedEnemyData`; listed under the first world that spawns it | `EnemyDataAsset.Faction` | `EnemyDataAsset.CatalogSubtitle` |
| Bosses | a Boss phase's `BossPrototype` → `Enemy.EnemyData` | faction if any | `CatalogSubtitle`, else "Boss" |
| Rift Mutations | `OverallGameConfig.LevelUpConfig.RiftMutations` (the same pool level-ups, Chests and the Cursed Rift roll from) | `Rarity` (colors: `CatalogTab.rarityColors`) | `Scope` (Player/Run) |

Name, description and icon are read from the same asset (`EnemyName`/`Description`/`ViewPrefab` sprite, `BossDataAsset.UiSprite`,
`UpgradeData.DisplayName`/`Icon`/`GetDescription()`).

**Adding an enemy to a world's spawns or a mutation to `LevelUpConfig.RiftMutations` is all it takes to show it.**

## View-only fields on the gameplay assets
In the `.View.cs` partials (not `.qtn`, so no codegen):
- `EnemyDataAsset.CatalogSubtitle` (also on bosses) and `EnemyDataAsset.Discovery`.
- `RiftMutationData.Discovery`.

`CatalogDiscovery`: `OnEncounter` (default, `???` until discovered), `AlwaysKnown`, `HiddenUntilFound` (not listed
until discovered, and not counted).

## Progress (persistence seam)
`CatalogProgress.Get(AssetGuid)` returns a `CatalogRecord` (Discovered / Encountered / Defeated / BestClearSeconds),
keyed by the asset's own Quantum `Guid`. **Nothing records it yet.** It returns an empty record, and
`CatalogTab.mockAllDiscovered` (on by default) reveals everything so the screen is usable. Next step: have the run write
encounters/kills/picks into `MetaProgressionSave` and read them in `Get`, then turn `mockAllDiscovered` off.

## Files
- `Assets/_Project/Scripts/UI/Menu/Catalog/CatalogTab.cs`: builds pages, sections, world filter, detail panel.
- `CatalogEntry.cs`: runtime entry plus the `Resolved*` presentation getters. `CatalogProgress.cs`: the progress seam.
- `CatalogTileWidget.cs`, `CatalogSectionWidget.cs`: the cloned UI templates.
- `Assets/_QuantumUser/Simulation/Assets/CatalogDiscovery.cs`: the discovery enum.

## History
Until 2026-10-27, every entry was a `CatalogEntryData` ScriptableObject under `Assets/_Project/Data/Catalog/`. The
Mutations page drifted (21 listed vs 42 in the pool). Those assets were deleted, and enemy subtitles were ported onto
`EnemyDataAsset.CatalogSubtitle`.
