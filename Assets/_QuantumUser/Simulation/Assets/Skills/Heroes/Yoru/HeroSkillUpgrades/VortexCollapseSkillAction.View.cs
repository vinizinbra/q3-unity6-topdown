namespace Quantum
{
    using UnityEngine;

    // View-only half of VortexCollapseSkillAction (see the partial declaration in
    // VortexCollapseSkillAction.cs).
    public partial class VortexCollapseSkillAction
    {
        [Tooltip("Blast VFX authored for a radius-1 explosion, scaled uniformly by Radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem BlastEffectPrefab;
    }
}
