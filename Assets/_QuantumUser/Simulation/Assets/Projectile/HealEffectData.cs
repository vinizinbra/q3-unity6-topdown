namespace Quantum
{
    using Photon.Deterministic;

    // Heals the target for a percent of its OWN MaxHealth - see HealUtility.ApplyHeal. Ignores
    // context.Damage entirely (unlike DamageEffectData/BurnEffectData) since heal strength isn't
    // meant to scale off whatever's seeding this pulse's Damage field.
    public unsafe class HealEffectData : HitEffectData
    {
        public FP HealPercent = FP._0_10;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            HealUtility.ApplyHeal(f, context.Target, context.Owner, HealPercent);
        }
    }
}
