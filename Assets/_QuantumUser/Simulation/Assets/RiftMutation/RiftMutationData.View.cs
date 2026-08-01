namespace Quantum
{
    using System;
    using UnityEngine;

    // View-only half of RiftMutationData (see the partial declaration in RiftMutationData.cs) -
    // same Description/DescriptionArgs/GetFormattedDescription shape as GlobalUpgradeData.View.cs/
    // WeaponPerkData.View.cs, so a retuned live value can't drift out of sync with the sentence
    // describing it.
    public abstract partial class RiftMutationData
    {
        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text - also shown as a level-up card's description (see GetDescription). Supports {0}, {1}, etc. placeholders filled in from this mutation's own live values via DescriptionArgs (override in a subclass), so a retuned number can't drift out of sync with the sentence describing it. Plain text with no placeholders works too.")]
        public string Description;

        // Override in a concrete RiftMutationData subclass to supply the values its own
        // Description template references via {0}, {1}, etc. - see GetFormattedDescription.
        protected virtual object[] DescriptionArgs => Array.Empty<object>();

        public string GetFormattedDescription() => DescriptionUtility.Format(Description, DescriptionArgs);
    }
}
