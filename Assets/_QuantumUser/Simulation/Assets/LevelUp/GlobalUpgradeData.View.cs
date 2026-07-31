namespace Quantum
{
    using System;
    using UnityEngine;

    // View-only half of GlobalUpgradeData (see the partial declaration in GlobalUpgradeData.cs) -
    // same Description/DescriptionArgs/GetFormattedDescription shape as WeaponPerkData.View.cs/
    // SkillActionData.View.cs, so a retuned Multiplier/Chance/Charges/RegenAmount can't drift out
    // of sync with the sentence describing it.
    public abstract partial class GlobalUpgradeData
    {
        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text - also shown as a level-up card's description (see GetDescription). Supports {0}, {1}, etc. placeholders filled in from this upgrade's own live values via DescriptionArgs (override in a subclass), so a retuned number can't drift out of sync with the sentence describing it. Plain text with no placeholders works too.")]
        public string Description;

        // Override in a concrete GlobalUpgradeData subclass to supply the values its own
        // Description template references via {0}, {1}, etc. - see GetFormattedDescription.
        protected virtual object[] DescriptionArgs => Array.Empty<object>();

        public string GetFormattedDescription() => DescriptionUtility.Format(Description, DescriptionArgs);
    }
}
