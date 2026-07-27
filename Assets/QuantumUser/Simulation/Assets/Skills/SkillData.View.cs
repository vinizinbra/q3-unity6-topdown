namespace Quantum
{
    using UnityEngine;

    // View-only half of SkillData (see the partial declaration in SkillData.cs) - lives on the
    // shared abstract base since every concrete skill type (Dash, Berserk, Projectile, ...) wants
    // the same "which icon represents this skill" concept, not a per-subclass field.
    public partial class SkillData
    {
        public Sprite Icon;

        [TextArea(2, 4)]
        [Tooltip("Player-facing effect text - shown wherever this skill is listed (tooltips, level-up choices). Authored, not generated from the skill's numbers, so it stays in sync only if you update it when you retune the asset.")]
        public string Description;
    }
}
