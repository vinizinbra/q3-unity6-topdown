namespace Quantum
{
    using UnityEngine;

    // View-only half of WeaponDataAsset (see the partial declaration in WeaponDataAsset.cs).
    public partial class WeaponDataAsset
    {
        [Tooltip("Prefab instantiated under the player's weapon socket to represent this weapon - must have a WeaponView component. WeaponViewController resolves this directly, no separate catalog/lookup needed.")]
        public GameObject ViewPrefab;
    }
}
