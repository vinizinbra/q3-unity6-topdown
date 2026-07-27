namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade for Berserk specifically - unlike BurnOnHitSkillAction/
    // MarkExplosiveDeathSkillAction (generic, work on any skill), this reads BerserkSkillData's own
    // FireRateBonus/MoveSpeedBonus/ReloadSpeedBonus at Begin so RageOverdriveUtility's correction
    // can double Berserk's own bonuses specifically, not stack an independent buff on top. Begin
    // grants a fresh RageOverdrive component (rage doesn't carry over between activations); End
    // reverts the correction if it triggered and removes the component, so a stray landed hit after
    // Berserk ends can't keep building toward Overdrive with nothing left to correct.
    public unsafe partial class RageOverdriveSkillAction : SkillActionData
    {
        public byte MaxStacks = 10;

        // Doubles Berserk's own bonus percentages by default - see RageOverdriveUtility.Correction.
        public FP OverdriveMultiplier = FP._2;

        public RageOverdriveSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                Begin(f, filter.Entity, skill);
            }
            else
            {
                End(f, filter.Entity);
            }
        }

        private void Begin(Frame f, EntityRef entity, SkillData skill)
        {
            f.AddOrGet<RageOverdrive>(entity, out var rage);
            rage->Stacks = 0;
            rage->MaxStacks = MaxStacks;
            rage->OverdriveMultiplier = OverdriveMultiplier;
            rage->Overdriven = false;

            if (skill is BerserkSkillData berserk)
            {
                rage->FireRateBonus = berserk.FireRateBonus;
                rage->MoveSpeedBonus = berserk.MoveSpeedBonus;
                rage->ReloadSpeedBonus = berserk.ReloadSpeedBonus;
            }
            else
            {
                Log.Error($"[Skill] {entity}'s RageOverdrive is attached to {skill.Name}, not Berserk - nothing to double");
            }

            Log.Debug($"[Skill] {entity} granted RageOverdrive (0/{MaxStacks} stacks)");
        }

        private void End(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<RageOverdrive>(entity, out var rage) == true)
            {
                RageOverdriveUtility.Revert(f, entity, rage);
            }

            f.Remove<RageOverdrive>(entity);

            Log.Debug($"[Skill] {entity}'s RageOverdrive ended");
        }
    }
}
