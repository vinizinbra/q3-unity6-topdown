namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, any enemy the owner lands a hit on is itself marked to
    // explode whenever it eventually dies (see DamageUtility.TryMarkExplodeOnDeath/ExplodeOnDeath).
    // Generic rather than tied to any one hero's skill - Max can put this on his Berserk Upgrades[]
    // slot (marking whoever he shoots while it's active) and Pixie can put the exact same asset on
    // her bomb's (marking whoever the blast catches); both grant the same MarkExplosiveDeath tag,
    // read the same way regardless of which skill activation granted it. Begin grants the stack, End
    // revokes it - only the activation currently in flight when this fires is affected.
    public unsafe partial class MarkExplosiveDeathSkillAction : SkillActionData
    {
        public MarkExplosiveDeathSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<MarkExplosiveDeath>(filter.Entity, out var mark);
                mark->Stacks++;

                // Defensive seed for a brand-new component - BonusRadiusMultiplier/
                // BonusDamageMultiplier/HeavyPayloadMultiplier all default to 0 on a fresh AddOrGet,
                // which would otherwise zero out TryExplodeOnDeath's own blastRadius/damage
                // computation (see DamageUtility.TryExplodeOnDeath) the very first time Max's own
                // Berserk grants this - he never goes through Pixie's ChainReactionPassiveData.Apply,
                // which is what seeds all three to 1 for her at spawn. No-op for Pixie, whose values
                // here are always already >0 by the time any skill of hers could run.
                if (mark->BonusRadiusMultiplier <= FP._0) mark->BonusRadiusMultiplier = FP._1;
                if (mark->BonusDamageMultiplier <= FP._0) mark->BonusDamageMultiplier = FP._1;
                if (mark->HeavyPayloadMultiplier <= FP._0) mark->HeavyPayloadMultiplier = FP._1;
            }
            // Guarded against underflow - Stacks is an unsigned Byte, so a spurious End with no
            // matching Begin (should no longer be reachable now that SkillSystem.AddUpgrade rejects
            // grants into a non-Ready slot, but this is cheap insurance against it happening some
            // other way) would otherwise wrap 0 down to 255 and leave every future hit marked
            // forever, since TryMarkExplodeOnDeath only skips marking when Stacks == 0.
            else if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(filter.Entity, out var mark) == true && mark->Stacks > 0)
            {
                mark->Stacks--;
            }
        }
    }
}
