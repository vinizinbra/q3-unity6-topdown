namespace Quantum
{
    using UnityEngine;

    // View-only half of CompressionSkillAction (see the partial declaration in
    // CompressionSkillAction.cs) - used only by rank 3's Implosion pulse.
    public partial class CompressionSkillAction
    {
        [Tooltip("Blast VFX authored for a radius-1 explosion, scaled uniformly by Radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem BlastEffectPrefab;
    }
}
