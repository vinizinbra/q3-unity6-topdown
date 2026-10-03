using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
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

        // Null for an out-of-range index or an empty slot.
        public WorldDefinition Get(int index) => index >= 0 && index < Worlds.Count ? Worlds[index] : null;
    }
}
