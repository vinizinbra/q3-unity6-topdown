namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Reaction point for the handful of Rift Mutations that need more than a one-shot CharacterStats
    // bake - mirrors WeaponPerkReactionSystem's shape (a single system dispatching off signals) but
    // reacts to CharacterStats fields instead of Weapon fields, and - unlike WeaponPerkReactionSystem's
    // OnCriticalHit handler - is NOT gated to DamageSource.Weapon, since these are character-level
    // effects meant to fire on any crit source (Weapon or Skill alike). See docs/rift-mutations.md.
    [Preserve]
    public unsafe class RiftMutationReactionSystem : SystemMainThread, ISignalOnCriticalHit, ISignalOnSkillActivated, ISignalOnShieldBroken
    {
        public override void Update(Frame f)
        {
        }

        // Critical Focus - flat cooldown seconds refunded on BOTH Hero Skill and Dash per crit (one
        // merged mutation, not two independent picks - Rift Mutations don't stack). Reuses
        // SkillSystem.ReduceCooldown, the same method Combat Reboot (weapon perk) and Rapid
        // Recycling (Lux's Scrap ascension) already call.
        public void OnCriticalHit(Frame f, EntityRef target, EntityRef owner, FP damage, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false
                || stats->CritSkillCooldownReduction <= FP._0
                || f.Unsafe.TryGetPointer<CharacterSkills>(owner, out var skills) == false)
                return;

            SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, stats->CritSkillCooldownReduction);
            SkillSystem.ReduceCooldown(f, skills, SkillSlotId.DashSkill, stats->CritSkillCooldownReduction);
        }

        // Infinite Momentum - every Dash activation drains CharacterStats.DashShieldCost off Shield
        // first, spilling any remainder onto Health, clamped so Health can never drop below 1 from
        // this drain alone (the tradeoff is meant to hurt, not to kill).
        public void OnSkillActivated(Frame f, EntityRef entity, SkillSlotId slotId)
        {
            if (slotId != SkillSlotId.DashSkill)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false || stats->DashShieldCost <= FP._0)
                return;

            FP remainingCost = stats->DashShieldCost;

            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == true && shield->Current > FP._0)
            {
                FP drained = FPMath.Min(shield->Current, remainingCost);
                shield->Current -= drained;
                remainingCost -= drained;
            }

            if (remainingCost <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            // Max(1, ...) floors the drain, then Min(..., CurrentHealth) makes sure that floor can
            // never read as a heal if CurrentHealth was already below 1 from ordinary damage.
            health->CurrentHealth = FPMath.Min(health->CurrentHealth, FPMath.Max(FP._1, health->CurrentHealth - remainingCost));
        }

        // Shield Breaker - the instant this entity's own Shield breaks, refill one Dash charge
        // (CurrentStacks, capped at MaxStacks) so it's immediately usable - a proc, not a permanent
        // capacity increase like Dash Charge (Global Upgrade).
        public void OnShieldBroken(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false
                || stats->ShieldBreakGrantsDashCharge == false
                || f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return;

            if (skills->DashSkill.CurrentStacks < skills->DashSkill.MaxStacks)
            {
                skills->DashSkill.CurrentStacks++;
            }
        }
    }
}
