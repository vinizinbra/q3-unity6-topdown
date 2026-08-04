namespace Quantum
{
    using UnityEngine;

    // View-only half of QuantumRoundsWeaponPerkData (see the partial declaration in
    // QuantumRoundsWeaponPerkData.cs).
    public partial class QuantumRoundsWeaponPerkData
    {
        [Tooltip("Impact spark played on the chained-onto enemy whenever this perk actually fires (see DirectHitData.ApplyQuantumRounds/EffectsManager.OnQuantumRoundsTriggered). Leave empty to fall back to EffectsManager's default area blast effect.")]
        public ParticleSystem ImpactEffectPrefab;
    }
}
