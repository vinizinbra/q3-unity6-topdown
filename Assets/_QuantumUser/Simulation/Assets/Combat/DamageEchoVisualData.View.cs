namespace Quantum
{
    using UnityEngine;

    // View-only half of DamageEchoVisualData (see the partial declaration in DamageEchoVisualData.cs).
    // Configure Kai's Ghost Shot particle (or any future Damage Echo source's own) directly here, per
    // asset instance - same pattern QuantumRoundsWeaponPerkData.View.cs uses for its own
    // ImpactEffectPrefab, resolved by EffectsManager.OnDamageEchoTriggered off the AssetRef the
    // triggering event carries.
    public partial class DamageEchoVisualData
    {
        [Tooltip("Played at the target when a Damage Echo lands (see EffectsManager.OnDamageEchoTriggered). Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem EffectPrefab;
    }
}
