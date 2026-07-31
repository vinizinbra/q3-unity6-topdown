namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Emergency Repair) - repairs the Sentry this Lux owns when the dash ends, if
    // it's within Radius of where the dash finished. Reuses the same "find this Lux's own Sentry"
    // lookup ScrapUtility.ApplyToOwnedSentry needs, just radius-gated and fired off the dash instead
    // of a Scrap pickup.
    public unsafe partial class RepairNearbyMachinesSkillAction : SkillActionData
    {
        public FP Radius = 6;
        public FP RepairFraction = FP._0_50;

        public RepairNearbyMachinesSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        // {0} = Radius, {1} = RepairFraction as a percent - e.g. "Repairs Sentries within {0} units
        // for {1}% of their max health when the dash ends."
        protected override object[] DescriptionArgs => new object[] { Radius, RepairFraction * 100 };

        public override FP EffectRadius => Radius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            FPVector3 position = filter.Transform3D->Position;
            var sentries = f.Filter<Sentry>();

            while (sentries.Next(out EntityRef sentryEntity, out Sentry sentry))
            {
                if (sentry.Owner != filter.Entity)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(sentryEntity, out var sentryTransform) == false)
                    continue;

                if ((sentryTransform->Position - position).SqrMagnitude > Radius * Radius)
                    continue;

                if (f.Unsafe.TryGetPointer<Health>(sentryEntity, out var health) == false)
                    continue;

                HealUtility.ApplyFlatHeal(f, sentryEntity, filter.Entity, health, health->MaxHealth * RepairFraction);
            }
        }
    }
}
