namespace Quantum
{
    using UnityEngine;

    // View-only half of GroundPoundPassiveUpgradeData (see the partial declaration in
    // GroundPoundPassiveUpgradeData.cs).
    public partial class GroundPoundPassiveUpgradeData
    {
        [Tooltip("Blast VFX authored for a radius-1 explosion, scaled uniformly by Radius when played. Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem BlastEffectPrefab;
    }
}
