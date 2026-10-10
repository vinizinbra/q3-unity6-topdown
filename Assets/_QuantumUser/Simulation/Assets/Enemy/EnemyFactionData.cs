namespace Quantum
{
    using UnityEngine;

    // Race / faction an enemy belongs to (Rukks, Security Robots, Wildlife, Black Moles...). A plain
    // ScriptableObject rather than an AssetObject: it is presentation/grouping data only, never read by
    // the simulation. Referenced from EnemyDataAsset.Faction (EnemyDataAsset.View.cs); the Catalog groups
    // enemies into sections by it and tints each section header with AccentColor.
    [CreateAssetMenu(fileName = "EnemyFaction", menuName = "Quantum/Enemy Faction")]
    public class EnemyFactionData : ScriptableObject
    {
        public string DisplayName;

        [Tooltip("Section header color in the Catalog.")]
        public Color AccentColor = new Color(0.992f, 0.224f, 0.443f);

        [Tooltip("Lower sorts first among the factions of a world.")]
        public int SortOrder;
    }
}
