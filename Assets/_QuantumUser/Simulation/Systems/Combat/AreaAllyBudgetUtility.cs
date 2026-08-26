namespace Quantum
{
    using Photon.Deterministic;

    // Read/spend side of AreaAllyBudget (see that component) - the generic "this deployable may only
    // ever give any one ally N total of X" primitive. Hero-agnostic: it resolves a slot for an ally
    // and clamps a requested amount against whatever that ally has already been given by this exact
    // area entity.
    //
    // Every method no-ops permissively (returns the full request) when the area carries no
    // AreaAllyBudget at all, or when the relevant cap is 0 - so an area that never opted in behaves
    // exactly as it did before this existed.
    public static unsafe class AreaAllyBudgetUtility
    {
        // Returns how much of `requested` this area is still allowed to heal `ally` for, and books
        // it. requested/the cap are both in ABSOLUTE HP (the caller converts its own percent against
        // the ally's MaxHealth first), so an area shared by allies with different MaxHealth caps each
        // of them relative to their own pool.
        public static FP ConsumeHeal(Frame f, EntityRef area, EntityRef ally, FP requested, FP allyMaxHealth)
        {
            if (requested <= FP._0)
                return FP._0;

            if (area == EntityRef.None)
                return requested; // no deployable behind this hit (a hitscan shot, a one-shot blast)

            if (f.Unsafe.TryGetPointer<AreaAllyBudget>(area, out var budget) == false
                || budget->MaxHealFractionPerAlly <= FP._0 || allyMaxHealth <= FP._0)
                return requested;

            if (TryResolveSlot(budget, ally, out int slot) == false)
                return requested; // no free slot (>4 allies) - let it through rather than silently denying

            FP cap = allyMaxHealth * budget->MaxHealFractionPerAlly;
            FP allowed = FPMath.Clamp(cap - budget->Healed[slot], FP._0, requested);

            budget->Healed[slot] += allowed;
            return allowed;
        }

        // Cooldown-reduction counterpart - same shape, in absolute seconds.
        public static FP ConsumeCooldownReduction(Frame f, EntityRef area, EntityRef ally, FP requested)
        {
            if (requested <= FP._0)
                return FP._0;

            if (area == EntityRef.None)
                return requested;

            if (f.Unsafe.TryGetPointer<AreaAllyBudget>(area, out var budget) == false
                || budget->MaxCooldownReductionPerAlly <= FP._0)
                return requested;

            if (TryResolveSlot(budget, ally, out int slot) == false)
                return requested;

            FP allowed = FPMath.Clamp(budget->MaxCooldownReductionPerAlly - budget->CooldownReduced[slot], FP._0, requested);

            budget->CooldownReduced[slot] += allowed;
            return allowed;
        }

        // Free Hit Guard counterpart - "may this area grant `ally` another guard?", booking it if so.
        //
        // Deliberately DENIES rather than permits when the area carries no budget or authors
        // MaxGuardsPerAlly 0, the opposite of the two FP caps above. A guard is a discrete grant that
        // must be opted into; an unbudgeted area handing out unlimited hit denials every tick is the
        // failure mode, not the safe default.
        public static bool TryConsumeGuard(Frame f, EntityRef area, EntityRef ally)
        {
            if (area == EntityRef.None)
                return false;

            if (f.Unsafe.TryGetPointer<AreaAllyBudget>(area, out var budget) == false
                || budget->MaxGuardsPerAlly == 0)
                return false;

            if (TryResolveSlot(budget, ally, out int slot) == false)
                return false; // no free slot (>4 allies) - deny, for the same reason as above

            if (budget->GuardsGranted[slot] >= budget->MaxGuardsPerAlly)
                return false;

            budget->GuardsGranted[slot]++;
            return true;
        }

        // Gives back allowance a caller booked but couldn't actually spend (e.g. a cooldown reduction
        // that landed on an already-ready skill) - clamped at 0, so an over-refund can never hand out
        // more than was booked in the first place.
        public static void RefundCooldownReduction(Frame f, EntityRef area, EntityRef ally, FP amount)
        {
            if (amount <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<AreaAllyBudget>(area, out var budget) == false
                || budget->MaxCooldownReductionPerAlly <= FP._0)
                return;

            if (TryResolveSlot(budget, ally, out int slot) == false)
                return;

            budget->CooldownReduced[slot] = FPMath.Max(FP._0, budget->CooldownReduced[slot] - amount);
        }

        // Find-or-claim, never evict: a slot claimed by an ally is theirs for this area's whole
        // lifetime, so leaving and re-entering the radius resumes their spent allowance rather than
        // handing them a fresh one (which would defeat the cap entirely). Capacity 4 matches the
        // co-op player cap, so overflow can't happen in a real match.
        private static bool TryResolveSlot(AreaAllyBudget* budget, EntityRef ally, out int slot)
        {
            for (int i = 0; i < budget->Ally.Length; i++)
            {
                if (budget->Ally[i] == ally)
                {
                    slot = i;
                    return true;
                }
            }

            for (int i = 0; i < budget->Ally.Length; i++)
            {
                if (budget->Ally[i] != EntityRef.None)
                    continue;

                budget->Ally[i] = ally;
                budget->Healed[i] = FP._0;
                budget->CooldownReduced[i] = FP._0;
                budget->GuardsGranted[i] = 0;
                slot = i;
                return true;
            }

            slot = -1;
            return false;
        }
    }
}
