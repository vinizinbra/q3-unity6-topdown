namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Reloading Slide) - restores part of the current weapon magazine after
    // dashing. Direct Weapon->Ammo top-up, no new mechanism needed.
    public unsafe partial class ReloadingSlideSkillAction : SkillActionData
    {
        public FP RestoreFraction = FP._0_25;

        public ReloadingSlideSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        protected override object[] DescriptionArgs => new object[] { RestoreFraction * 100 };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(filter.Entity, out var weapon) == false)
                return;

            int restoreAmount = FPMath.CeilToInt(weapon->MagazineSize * RestoreFraction);
            int newAmmo = weapon->Ammo + restoreAmount;
            weapon->Ammo = newAmmo > weapon->MagazineSize ? weapon->MagazineSize : newAmmo;

            // Bypasses WeaponSystem's own reload path entirely, so its own WeaponReloaded fire
            // (see WeaponSystem.cs) never happens for this top-up - fired directly here instead so
            // this reads identically to a normal reload. No View currently subscribes to
            // WeaponReloaded at all (a pre-existing gap, not specific to this perk) - fired anyway so
            // this doesn't silently regress once one exists.
            f.Events.WeaponReloaded(filter.Entity);
        }
    }
}
