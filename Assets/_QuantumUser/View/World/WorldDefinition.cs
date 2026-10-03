using UnityEngine;

namespace Quantum
{
    // One playable world: what it looks like (Theme, view-only) and what it plays like (the sim assets
    // the menu writes onto RuntimeConfig before the match starts). Listed in order on WorldCatalog -
    // the list index is what RuntimeConfig.World carries to every client. See docs/worlds.md.
    [CreateAssetMenu(fileName = "World", menuName = "Quantum/View/World Definition")]
    public class WorldDefinition : ScriptableObject
    {
        [Tooltip("Shown in the menu's world picker.")]
        public string DisplayName;
        public Sprite Icon;

        [Header("Look (view-only)")]
        [Tooltip("Sky, tileset, water, blood colour - applied by EnvironmentManager when the match loads.")]
        public WorldTheme Theme;

        [Header("Play (written onto RuntimeConfig)")]
        [Tooltip("This world's Director timeline (phases, rosters, elites, boss).")]
        public AssetRef<SurvivalConfig> SurvivalConfig;

        [Tooltip("Optional fine-tuning on top of the global BalanceConfig (e.g. a harder world). Leave empty for 1x.")]
        public AssetRef<WorldBalanceData> Balance;

        public void ApplyTo(RuntimeConfig config, int worldIndex)
        {
            config.World = worldIndex;
            config.WorldBalance = Balance;

            if (SurvivalConfig.IsValid)
                config.SurvivalConfig = SurvivalConfig;
        }
    }
}
