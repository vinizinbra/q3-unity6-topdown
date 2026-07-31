namespace Quantum
{
    using System;
    using UnityEngine;

    // View-only half of WeaponPerkData (see the partial declaration in WeaponPerkData.cs). Read off
    // Weapon.Perks, which is why a roll records its perks rather than only baking them. Icon/
    // DisplayName come from the shared UpgradeData base. Same Description/DescriptionArgs/
    // GetFormattedDescription shape as SkillActionData.View.cs - a retuned Multiplier/Chance/Bonus
    // can't drift out of sync with the sentence describing it, since the sentence is filled in from
    // those same live fields rather than hand-typed separately.
    public abstract partial class WeaponPerkData
    {
        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text - also shown as a level-up/drop card's description (see GetDescription). Supports {0}, {1}, etc. placeholders filled in from this perk's own live values via DescriptionArgs (override in a subclass), so a retuned number can't drift out of sync with the sentence describing it. Plain text with no placeholders works too.")]
        public string Description;

        // Override in a concrete WeaponPerkData subclass to supply the values its own Description
        // template references via {0}, {1}, etc. - see GetFormattedDescription.
        protected virtual object[] DescriptionArgs => Array.Empty<object>();

        public string GetFormattedDescription() => DescriptionUtility.Format(Description, DescriptionArgs);
    }
}
