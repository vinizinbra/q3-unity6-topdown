namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Cursed Survival's own reversible player-state override - pins every participant to 1 HP and
    // disables (never breaks - see AccessoryGuardUtility.Disable's own destructive comment) their
    // Accessory for the challenge's duration, then restores the exact pre-challenge values on every
    // terminal exit (success, failure, or interruption - all funneled through TeamChallengeUtility.
    // End, which calls RestoreCurse unconditionally).
    public static unsafe class CursedSurvivalUtility
    {
        public static void ApplyCurse(Frame f, EntityRef poi)
        {
            var filtered = f.Filter<TeamChallengeParticipant>();

            while (filtered.Next(out EntityRef entity, out TeamChallengeParticipant participant))
            {
                if (participant.Poi == poi)
                    ApplyCurseTo(f, poi, entity);
            }
        }

        private static void ApplyCurseTo(Frame f, EntityRef poi, EntityRef entity)
        {
            if (f.Has<CursedSurvivalOverride>(entity) == true)
                return; // already cursed - never re-snapshot over a live curse

            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            // Optional - not guaranteed on every entity that could theoretically be a participant.
            f.Unsafe.TryGetPointer<AccessoryGuard>(entity, out var guard);

            f.Add<CursedSurvivalOverride>(entity, out var overrideState);
            overrideState->Poi = poi;
            overrideState->PreviousCurrentHealth = health->CurrentHealth;
            overrideState->PreviousAccessoryDisabled = guard != null && guard->Disabled;

            health->CurrentHealth = FP._1;

            // TryBlock's own `if (guard->Disabled == true) return false;` early-return is the only
            // thing this flag needs to touch - CurrentDurability/MaxDurability/State are never read
            // or written by this path, unlike the permanent, destructive AccessoryGuardUtility.
            // Disable(), so there is nothing else to snapshot or restore for the Accessory half.
            if (guard != null)
                guard->Disabled = true;

            Log.Debug($"[TeamChallenge] Cursed Survival curse applied to {entity} (was {overrideState->PreviousCurrentHealth} HP)");
        }

        public static void RestoreCurse(Frame f, EntityRef poi)
        {
            List<EntityRef> toRestore = new List<EntityRef>();
            var filtered = f.Filter<CursedSurvivalOverride>();

            while (filtered.Next(out EntityRef entity, out CursedSurvivalOverride overrideState))
            {
                if (overrideState.Poi == poi)
                    toRestore.Add(entity);
            }

            for (int i = 0; i < toRestore.Count; i++)
            {
                RestoreCurseFrom(f, toRestore[i]);
            }
        }

        // Restores from the IMMUTABLE snapshot taken at ApplyCurseTo, never from whatever the live
        // pinned value happens to be - this is what prevents a mid-curse heal or Accessory recovery
        // attempt from leaving the player better off after the curse ends than before it began.
        private static void RestoreCurseFrom(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CursedSurvivalOverride>(entity, out var overrideState) == false)
                return;

            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == true)
                health->CurrentHealth = overrideState->PreviousCurrentHealth;

            if (f.Unsafe.TryGetPointer<AccessoryGuard>(entity, out var guard) == true)
                guard->Disabled = overrideState->PreviousAccessoryDisabled;

            Log.Debug($"[TeamChallenge] Cursed Survival curse restored for {entity} (back to {overrideState->PreviousCurrentHealth} HP)");

            f.Remove<CursedSurvivalOverride>(entity);
        }
    }
}
