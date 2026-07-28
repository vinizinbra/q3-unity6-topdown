namespace Quantum
{
    using UnityEngine;

    // View-only half of EnemyDataAsset (see the partial declaration in EnemyDataAsset.cs).
    public partial class EnemyDataAsset
    {
        [Tooltip("Tint applied to EffectsManager's shared death explosion VFX when this enemy type explodes on death (Tier == Filler - see DamageUtility.ApplyDamage). Unused for other tiers, which still play the lingering die animation instead.")]
        public Color ExplosionColor = Color.white;

        [PreviewPrefab]
        [Tooltip("Visual rig prefab (EnemyViewRig + SpriteRenderers + optional weapon) instantiated as a child on spawn (EnemyView) - swap this per enemy type to change appearance without touching the shared generic sim prototype. EnemyBlobAnimationView/EnemyArmAimView/EnemyAttackVisualsView live on the generic prototype itself, not in here - they get this rig handed to them once it's instantiated. Scale is fit automatically off EnemyViewRig.ReferenceSprite's bounds, so it's PPU-independent - no need to hand-author at a specific scale.")]
        public GameObject ViewPrefab;
    }
}
