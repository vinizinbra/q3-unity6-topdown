namespace Quantum
{
    using System;
    using UnityEngine;

    // View-only half of SkillActionData (see the partial declaration in SkillActionData.cs) - lives
    // on the shared abstract base since every concrete action/upgrade (SpawnEntitySkillAction,
    // JuggernautLandingRootSkillAction, IncreaseDamageSkillAction, ...) wants the same "what does
    // this actually do" documentation slot, not a per-subclass field. Same shape as
    // SkillData.Description. Player-facing when offered as a LevelUpPoolKind.SkillUpgrade card, via
    // GetDescription() (see SkillActionData.cs) - not just a designer note, since the template
    // substitution below keeps it accurate to this asset's own live-tuned numbers.
    public partial class SkillActionData
    {
        [TextArea(2, 4)]
        [Tooltip("Effect text - also shown to players as a level-up choice card's description (see GetDescription). Supports {0}, {1}, etc. placeholders filled in from this action's own live values via DescriptionArgs (override in a subclass), so a retuned number can't drift out of sync with the sentence describing it. Plain text with no placeholders works too.")]
        public string Description;

        // Override in a concrete SkillActionData subclass to supply the values its own Description
        // template references via {0}, {1}, etc. - see GetFormattedDescription.
        protected virtual object[] DescriptionArgs => Array.Empty<object>();

        public string GetFormattedDescription() => DescriptionUtility.Format(Description, DescriptionArgs);
    }
}
