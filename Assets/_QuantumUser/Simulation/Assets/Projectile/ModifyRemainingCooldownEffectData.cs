namespace Quantum
{
    using Photon.Deterministic;

    // Generic "reduce the target's own REMAINING skill cooldown" hit effect - the reusable primitive
    // the spec asks for in place of hero-specific cooldown code. Zara's Sound Boost rank 2 is the
    // first consumer (a Support Beat shaving time off every affected ally's Hero Skill), but nothing
    // here is Zara-shaped: any effects list on any area/projectile can carry one.
    //
    // Deliberately "remaining cooldown only": clamped at 0 by SkillSystem.ReduceCooldown, so it can
    // never go negative or bank into a future cooldown, and it does nothing at all to an ally whose
    // skill is already up.
    //
    // Respects the spawning area's own AreaAllyBudget cap when RespectAreaBudget is on (see
    // AreaAllyBudgetUtility) - that's what backs "MaxHeroSkillCooldownReductionPerTotem", without
    // this class knowing what a Totem is. The budget is charged only for reduction that ACTUALLY
    // landed, so an already-ready skill never eats into the allowance.
    public unsafe class ModifyRemainingCooldownEffectData : HitEffectData
    {
        public SkillSlotId Slot = SkillSlotId.HeroSkill;

        // Seconds removed per application. Positive reduces; this never extends a cooldown (a
        // negative value is treated as no-op by SkillSystem.ReduceCooldown).
        public FP Amount = FP._0_50;

        // On (the default) charges HitEffectContext.SourceEntity's AreaAllyBudget, capping how much
        // any one deployable instance can ever give any one ally. Off = uncapped per application,
        // for a one-shot source with no instance to budget against.
        public bool RespectAreaBudget = true;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None || Amount <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(context.Target, out var skills) == false)
                return;

            FP requested = Amount;

            if (RespectAreaBudget == true && context.SourceEntity != EntityRef.None)
            {
                // Peek first, then reconcile: the budget is charged for the request, and whatever
                // ReduceCooldown couldn't actually use is refunded below, so a full allowance is
                // never consumed by an ally whose skill was already off cooldown.
                requested = AreaAllyBudgetUtility.ConsumeCooldownReduction(f, context.SourceEntity, context.Target, requested);

                if (requested <= FP._0)
                    return;
            }

            FP applied = SkillSystem.ReduceCooldown(f, skills, Slot, requested);

            if (RespectAreaBudget == true && context.SourceEntity != EntityRef.None && applied < requested)
            {
                AreaAllyBudgetUtility.RefundCooldownReduction(f, context.SourceEntity, context.Target, requested - applied);
            }
        }
    }
}
