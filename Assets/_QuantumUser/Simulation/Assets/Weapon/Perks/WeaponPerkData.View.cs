namespace Quantum
{
    using UnityEngine;

    // View-only half of WeaponPerkData (see the partial declaration in WeaponPerkData.cs). Read off
    // Weapon.Perks, which is why a roll records its perks rather than only baking them. Icon/
    // DisplayName come from the shared UpgradeData base.
    public abstract partial class WeaponPerkData
    {
        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text. Authored, not generated from the perk's numbers - so it stays in sync only if you update it when you retune Multiplier/Chance.")]
        public string Description;
    }
}
