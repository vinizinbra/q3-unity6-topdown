using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
    public static class WorldDefinitionExtensions
    {
        // WorldDefinition.Theme is a plain ScriptableObject (simulation assembly can't see WorldTheme).
        public static WorldTheme GetTheme(this WorldDefinition world) => world != null ? world.Theme as WorldTheme : null;
    }

    // Ordered list of every playable world (index 0 = World 1). Lives at
    // Resources/Worlds/WorldCatalog so both the menu (world picker) and the gameplay scene
    // (EnvironmentManager) can reach it without a scene reference. See docs/worlds.md.
    [CreateAssetMenu(fileName = "WorldCatalog", menuName = "Quantum/View/World Catalog")]
    public class WorldCatalog : ScriptableObject
    {
        private const string ResourcePath = "Worlds/WorldCatalog";

        public List<WorldDefinition> Worlds = new();

        private static WorldCatalog _instance;

        public static WorldCatalog Instance => _instance != null ? _instance : _instance = Resources.Load<WorldCatalog>(ResourcePath);

        public int IndexOf(WorldDefinition world) => world != null ? Worlds.IndexOf(world) : -1;

        // Null for an out-of-range index or an empty slot.
        public WorldDefinition Get(int index) => index >= 0 && index < Worlds.Count ? Worlds[index] : null;
    }
}
