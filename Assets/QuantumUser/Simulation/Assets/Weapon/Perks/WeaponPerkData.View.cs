namespace Quantum
{
    using UnityEngine;

    // View-only half of WeaponPerkData (see the partial declaration in WeaponPerkData.cs). Read off
    // Weapon.Perks, which is why a roll records its perks rather than only baking them.
    public abstract partial class WeaponPerkData
    {
        [Tooltip("Shown wherever this perk is listed - weapon tooltips, drop comparisons, level-up choices.")]
        public Sprite Icon;

        [Tooltip("Player-facing perk name. The asset name is not used as a fallback.")]
        public string DisplayName;

        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text. Authored, not generated from the perk's numbers - so it stays in sync only if you update it when you retune Multiplier/Chance.")]
        public string Description;
    }
}
