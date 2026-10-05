using UnityEngine;

namespace Quantum
{
    // One playable world: what it looks like (Theme, view-only - the sim never reads it) and what it plays
    // like (SurvivalConfig/Balance). A Quantum asset so RuntimeConfig.WorldDefinition can reference it
    // directly; listed on WorldCatalog for the menu picker (the list index is what the party syncs).
    // See docs/worlds.md.
    [CreateAssetMenu(fileName = "World", menuName = "Quantum/View/World Definition")]
    public class WorldDefinition : AssetObject
    {
        [Tooltip("Shown in the menu's world picker.")]
        public string DisplayName;
        public Sprite Icon;

        [Header("Look (view-only)")]
        [Tooltip("Assign a WorldTheme (sky, tileset, water, blood colour - applied by EnvironmentManager when the match loads). Typed as ScriptableObject only because the simulation assembly can't see the view-side WorldTheme; read it with GetTheme().")]
        public ScriptableObject Theme;

        [Header("Play (written onto RuntimeConfig)")]
        [Tooltip("This world's Director timeline (phases, rosters, elites, boss).")]
        public AssetRef<SurvivalConfig> SurvivalConfig;

        [Tooltip("Optional fine-tuning on top of the global BalanceConfig (e.g. a harder world). Leave empty for 1x.")]
        public AssetRef<WorldBalanceData> Balance;

        // Points `config` at this world and derives the sim-facing fields the Director/balance read.
        public void ApplyTo(RuntimeConfig config)
        {
            config.WorldDefinition = this;
            config.WorldBalance = Balance;

            if (SurvivalConfig.IsValid)
                config.SurvivalConfig = SurvivalConfig;
        }
    }
}
