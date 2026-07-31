namespace Quantum
{
    using UnityEngine;

    // View-only half of AreaHitData (see the partial declaration in AreaHitData.cs).
    public partial class AreaHitData
    {
        [Tooltip("Blast VFX authored for a radius-1 explosion, scaled uniformly by BlastRadius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem BlastEffectPrefab;
    }
}
