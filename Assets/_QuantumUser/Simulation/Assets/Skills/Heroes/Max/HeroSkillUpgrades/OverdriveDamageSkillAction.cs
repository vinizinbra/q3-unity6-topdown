namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - doubles (by default) CharacterStats.WeaponDamageMultiplier the moment
    // RageOverdrive.Overdriven flips true, checked every OnGoing tick since Overdrive can trigger
    // any time mid-activation from a landed hit (see RageOverdriveUtility.TryAdvanceStack), not at
    // a fixed lifecycle point. Reverts at End. Independent of RageOverdriveSkillAction - grant both
    // to double stats AND damage at max Rage, or either alone; this one only reads
    // RageOverdrive.Overdriven, never writes it.
    public unsafe partial class OverdriveDamageSkillAction : SkillActionData
    {
        public FP DamageMultiplier = FP._2;

        // {0} = DamageMultiplier as a raw multiplier - e.g. "...multiplies weapon damage by {0}x..."
        protected override object[] DescriptionArgs => new object[] { DamageMultiplier };

        public OverdriveDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                Begin(f, filter.Entity);
            }
            else if (firedPhase == SkillActionPhase.OnGoing)
            {
                TryApply(f, filter.Entity);
            }
            else
            {
                End(f, filter.Entity);
            }
        }

        private void Begin(Frame f, EntityRef entity)
        {
            f.AddOrGet<WeaponDamageOverdrive>(entity, out var overdrive);
            overdrive->Applied = false;
        }

        private void TryApply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<WeaponDamageOverdrive>(entity, out var overdrive) == false || overdrive->Applied == true)
                return;

            if (f.Unsafe.TryGetPointer<RageOverdrive>(entity, out var rage) == false || rage->Overdriven == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->WeaponDamageMultiplier *= DamageMultiplier;
            overdrive->Applied = true;

            Log.Debug($"[Skill] {entity} reached Rage Overdrive - weapon damage x{DamageMultiplier}");
        }

        private void End(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<WeaponDamageOverdrive>(entity, out var overdrive) == true && overdrive->Applied == true &&
                f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                stats->WeaponDamageMultiplier /= DamageMultiplier;
            }

            f.Remove<WeaponDamageOverdrive>(entity);
        }
    }
}
