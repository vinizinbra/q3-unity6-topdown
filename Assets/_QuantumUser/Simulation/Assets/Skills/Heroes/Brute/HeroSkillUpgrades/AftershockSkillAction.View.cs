namespace Quantum
{
    using UnityEngine;

    // View-only half of AftershockSkillAction (see the partial declaration in AftershockSkillAction.
    // cs). Carried forward from the deleted JuggernautEndExplosionSkillAction.View.cs.
    public partial class AftershockSkillAction
    {
        [Tooltip("Blast VFX authored for a radius-1 explosion, scaled uniformly by Radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem BlastEffectPrefab;
    }
}
