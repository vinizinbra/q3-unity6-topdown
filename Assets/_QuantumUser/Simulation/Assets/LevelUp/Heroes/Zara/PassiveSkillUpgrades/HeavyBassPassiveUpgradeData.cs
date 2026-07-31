namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the Resonance Pulse's damage, and raises its shockwave by one
    // KnockbackTier step (Small -> Medium -> Strong, capped at Strong) on top of the base passive's
    // own baseline (see ResonancePassiveData.KnockbackTier) - tiers are a fixed ladder, not a
    // stacking magnitude, so this steps up rather than adding a raw force bonus.
    public unsafe partial class HeavyBassPassiveUpgradeData : PassiveUpgradeData
    {
        public FP DamageBonus = 10;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            resonance->DamageAmount += DamageBonus;

            if (resonance->KnockbackTier < (byte)KnockbackTier.Strong)
            {
                resonance->KnockbackTier++;
            }
        }
    }
}
