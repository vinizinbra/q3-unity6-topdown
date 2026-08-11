namespace Quantum
{
    using UnityEngine;

    // View-only half of EnemyDeliveryData (see the partial declaration in EnemyDeliveryData.cs).
    // Lives on the delivery, not on HitEffectData - the same DamageEffectData asset can be reused by
    // a ground slam and a melee swing, and those should read visually distinct on impact even though
    // the numbers underneath are identical. EnemyAttackVisualsView resolves this off whichever
    // delivery is currently active (see HitEffectApplied/Events.qtn) rather than a global manager,
    // since it already tracks that per attacking enemy.
    public partial class EnemyDeliveryData
    {
        [Tooltip("Particle played at the position of a target this delivery actually hit. Leave empty for no hit-impact particle on this delivery.")]
        public ParticleSystem HitImpactPrefab;

        [Tooltip("World-space offset added to the hit position before spawning HitImpactPrefab - e.g. raising a ground-slam's dust burst to roughly chest height instead of at the floor. Same plain-offset convention as AttackVisualStep.Offset (no rotation applied). Zero by default (no change).")]
        public Vector3 HitImpactOffset;
    }
}
