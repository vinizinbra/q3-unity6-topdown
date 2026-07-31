namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - taking damage at maximum Adrenaline grants temporary Damage Reduction -
    // see AdrenalineUtility.OnDamageTaken/TryApplyTooAngryToDie.
    public unsafe partial class TooAngryToDiePassiveUpgradeData : PassiveUpgradeData
    {
        public FP DamageReductionAtMax = FP._0_25;
        public FP DamageReductionDuration = 3;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return;

            adrenaline->DamageReductionAtMax = DamageReductionAtMax;
            adrenaline->DamageReductionDuration = DamageReductionDuration;
        }
    }
}
