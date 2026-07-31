namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Portable Cover) - restores some of the caster's own Shield when the dash
    // ends, plus a smaller restore to any Sentry this Lux owns nearby. Simplified to an instant
    // restore (added to Shield.Current, clamped to Max) rather than a separate decaying bonus-shield
    // pool - "gain a Shield after dashing" reads the same from the player's perspective either way,
    // without needing a new expiring-capacity mechanic.
    public unsafe partial class PortableCoverSkillAction : SkillActionData
    {
        public FP ShieldRestoreAmount = 20;
        public FP MachineShieldRestoreAmount = 10;
        public FP MachineRadius = 6;

        public PortableCoverSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        // {0} = ShieldRestoreAmount, {1} = MachineShieldRestoreAmount
        protected override object[] DescriptionArgs => new object[] { ShieldRestoreAmount, MachineShieldRestoreAmount };

        public override FP EffectRadius => MachineRadius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (f.Unsafe.TryGetPointer<Shield>(filter.Entity, out var shield) == true)
            {
                ShieldUtility.ApplyFlatShield(f, filter.Entity, filter.Entity, shield, ShieldRestoreAmount);
            }

            FPVector3 position = filter.Transform3D->Position;

            RestoreNearbyMachines(f, filter.Entity, position);
        }

        private void RestoreNearbyMachines(Frame f, EntityRef owner, FPVector3 position)
        {
            var sentries = f.Filter<Sentry>();

            while (sentries.Next(out EntityRef sentryEntity, out Sentry sentry))
            {
                if (sentry.Owner != owner)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(sentryEntity, out var sentryTransform) == false)
                    continue;

                if ((sentryTransform->Position - position).SqrMagnitude > MachineRadius * MachineRadius)
                    continue;

                if (f.Unsafe.TryGetPointer<Shield>(sentryEntity, out var sentryShield) == false)
                    continue;

                ShieldUtility.ApplyFlatShield(f, sentryEntity, owner, sentryShield, MachineShieldRestoreAmount);
            }
        }
    }
}
