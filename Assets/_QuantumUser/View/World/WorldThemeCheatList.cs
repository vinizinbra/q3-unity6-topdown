namespace Quantum
{
    using UnityEngine;

    // The biomes CheatMenu -> World can switch to at runtime (EnvironmentManager.Load: sky, water,
    // blood colour, and a rebuild of every chunk's tileset). Loaded from
    // Resources/Debug/WorldThemeCheatList.asset - being in Resources is what makes every listed biome
    // (its tileset, models, textures) ship in the build even when no scene references it; a deliberate
    // trade-off (bigger build) chosen 2026-09-28 so biomes can be compared on device.
    [CreateAssetMenu(menuName = "RiftRaiders/Debug/World Theme Cheat List", fileName = "WorldThemeCheatList")]
    public class WorldThemeCheatList : ScriptableObject
    {
        [Tooltip("One CheatMenu button per entry, labelled with the asset name minus 'Theme'.")]
        public WorldTheme[] Themes;
    }
}
