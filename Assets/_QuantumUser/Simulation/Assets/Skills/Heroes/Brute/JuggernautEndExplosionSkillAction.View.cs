namespace Quantum
{
    using UnityEngine;

    // View-only half of JuggernautEndExplosionSkillAction (see the partial declaration in
    // JuggernautEndExplosionSkillAction.cs).
    public partial class JuggernautEndExplosionSkillAction
    {
        [Tooltip("Blast VFX authored for a radius-1 explosion, scaled uniformly by Radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem BlastEffectPrefab;
    }
}
