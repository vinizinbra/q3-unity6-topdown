namespace Quantum
{
    using Photon.Deterministic;

    // Vendetta Upgrade - extends how long a Vendetta mark lasts before it lapses. Additive on top
    // of VendettaPassiveData's own authored BaseMarkDuration, same shape as SettledScore's own
    // RevengeConfig composition but an additive bonus rather than a Max-of.
    public unsafe partial class BloodDebtPassiveUpgradeData : PassiveUpgradeData
    {
        public FP AdditionalDuration = 4;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<RevengeConfig>(entity, out var config);
            config->MarkDuration += AdditionalDuration;
        }
    }
}
