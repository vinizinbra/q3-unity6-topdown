namespace Quantum
{
    using System;
    using UnityEngine;

    // View-only half of SkillData (see the partial declaration in SkillData.cs) - lives on the
    // shared abstract base since every concrete skill type (Dash, Berserk, Projectile, ...) wants
    // the same "which icon represents this skill" concept, not a per-subclass field.
    public partial class SkillData
    {
        [PreviewSprite]
        public Sprite Icon;

        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text - shown wherever this skill is listed (tooltips, level-up choices). Supports {0}, {1}, etc. placeholders filled in from this skill's own live values via DescriptionArgs (override in a subclass), so a retuned number can't drift out of sync with the sentence describing it. Plain text with no placeholders works too.")]
        public string Description;

        // Override in a concrete SkillData subclass to supply the values its own Description
        // template references via {0}, {1}, etc. - see GetFormattedDescription.
        protected virtual object[] DescriptionArgs => Array.Empty<object>();

        public string GetFormattedDescription() => DescriptionUtility.Format(Description, DescriptionArgs);
    }
}
