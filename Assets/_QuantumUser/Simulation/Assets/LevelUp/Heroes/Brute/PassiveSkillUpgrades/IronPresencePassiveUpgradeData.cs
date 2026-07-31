namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - Intimidated enemies move slower and have reduced knockback resistance.
    // The slow reuses the existing, fully generic StatusEffectUtility.ApplyIce; the knockback-taken
    // side needed a new timed status (see StatusEffects.qtn) since nothing existing lets a status
    // affect an enemy's own knockback resistance directly.
    public unsafe partial class IronPresencePassiveUpgradeData : PassiveUpgradeData
    {
        public FP SlowMultiplier = FP._0_75;
        public FP KnockbackTakenMultiplier = FP._1_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            aura->IntimidateSlowMultiplier = SlowMultiplier;
            aura->IntimidateKnockbackTakenMultiplier = KnockbackTakenMultiplier;
        }
    }
}
