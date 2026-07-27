namespace Quantum
{
    using Photon.Deterministic;

    // Multiplies SkillSlot.AreaMultiplier - see the field's own comment on CharacterSkills.qtn.
    // Carries no area of its own; it only scales whichever HitPathSkillAction/SpawnEntitySkillAction
    // entries read the multiplier when they execute, so it needs to run before them within the same
    // phase - guaranteed here by defaulting Priority below every other action's default (0) rather
    // than relying on Actions/Upgrades list position.
    public unsafe partial class IncreaseAreaSkillAction : SkillActionData
    {
        public FP Multiplier = FP._1_50;

        public IncreaseAreaSkillAction()
        {
            Phase = SkillActionPhase.Begin;
            Priority = -100;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            slot->AreaMultiplier *= Multiplier;

            Log.Debug($"[Skill] {filter.Entity}'s area multiplier is now {slot->AreaMultiplier}");
        }
    }
}
