namespace Quantum
{
    using Photon.Deterministic;

    // Last Stand rank 3's ("Too Angry to Die") live gating check, hooked into DamageUtility.
    // ApplyDamage right where lethal Health damage would otherwise clamp to 0 (see that call site).
    // Generic mechanism - gated purely by CheatDeathGuard's presence, never an "is this entity Max"
    // check - but force-ending "the current Overdrive activation" only makes sense for whoever
    // actually has an active HeroSkill, which today is only ever Max/Berserk (see
    // LastStandSkillAction).
    public static unsafe class CheatDeathUtility
    {
        // Returns true if this hit was cheated - caller clamps Health to 1 instead of 0 when this
        // returns true, and leaves the normal death branch alone otherwise.
        public static bool TryPreventLethal(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CheatDeathGuard>(entity, out var guard) == false)
                return false;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return false;

            SkillSlot* heroSkill = SkillSystem.ResolveSlot(skills, SkillSlotId.HeroSkill);

            if (heroSkill == null || heroSkill->State != SkillState.Active)
                return false; // Overdrive isn't actually active right now - nothing to cheat death out of

            heroSkill->StateTimer = FP._0; // BerserkSkillData.Tick reads this next tick and ends Overdrive
            f.Events.CheatDeathTriggered(entity);

            // Rage is reverted and cleared right here, not left for BerserkSkillData.End to
            // discover next tick, so a Full Throttle/Ignition effect that was active at max Rage
            // can't linger for the one tick between this save and Overdrive actually ending.
            // RageOverdriveUtility.Revert + a direct zero (not ResetStacks) deliberately bypasses
            // Last Stand rank 2's own RageLossFraction, which a rank-3 holder always also carries and
            // which would otherwise leave most of the Rage standing - "consume/reset Rage" is part of
            // what this save costs. BerserkSkillData.End then parks that 0 for rank 1's carry-over,
            // so a cheated death genuinely spends the momentum.
            if (f.Unsafe.TryGetPointer<RageOverdrive>(entity, out var rage) == true)
            {
                RageOverdriveUtility.Revert(f, entity, rage);
                rage->Stacks = 0;
            }

            // Opens a brief window where DamageUtility's own Invulnerable check ignores every hit
            // outright, so whatever else lands this tick (or the next few) can't kill Max again
            // right through the 1 Health this save just left him at. StatusEffectSystem ticks
            // CheatDeathImmunityRemaining back down and removes the tag once it lapses.
            if (guard->ImmunityDuration > FP._0 && f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true)
            {
                f.Add<Invulnerable>(entity);
                status->CheatDeathImmunityRemaining = guard->ImmunityDuration;
            }

            Log.Debug($"[Skill] {entity} cheated death via Too Angry to Die - Overdrive forced to end");
            return true;
        }
    }
}
