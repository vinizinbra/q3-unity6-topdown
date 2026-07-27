namespace Quantum
{
    // Hero Skill Upgrade - while equipped, reaching max Rage (see RageOverdrive.Overdriven) makes
    // any real (ammo-depleted) reload take 0 time for the rest of the activation - see
    // WeaponSystem.StartReload/IsInstantReloadOverdriven. Begin grants the tag, End revokes it -
    // the actual gating check (is Overdriven true right now) happens live in WeaponSystem, not
    // here, since this only needs to say "is the upgrade equipped", not track any state of its own.
    public unsafe partial class OverdriveInstantReloadSkillAction : SkillActionData
    {
        public OverdriveInstantReloadSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<InstantReloadOverdrive>(filter.Entity, out _);
            }
            else
            {
                f.Remove<InstantReloadOverdrive>(filter.Entity);
            }
        }
    }
}
