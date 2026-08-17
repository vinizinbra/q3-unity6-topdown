namespace Quantum
{
    // Healing Shrine's own interaction - unlike Cursed Rift, this is a genuine one-shot: no
    // per-player session component, no Choice Window, no input lock. Pressing the (redirected)
    // Base Skill button while State == Available heals immediately and marks the shrine used for
    // that player under its own configured usage policy - same generic POI availability/usage
    // infra (PoiAvailabilityUtility/PoiUsageUtility) CursedRiftUtility's own resolver reads.
    public static unsafe class HealingShrineUtility
    {
        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution and Busy already happened there via the sibling Interactable component) -
        // the richer WHY behind whether the button would do anything right now, so the
        // world-space prompt can explain itself instead of silently hiding.
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef shrine)
        {
            if (f.Unsafe.TryGetPointer<HealingShrine>(shrine, out var healingShrine) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, healingShrine->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            if (PoiUsageUtility.CanUse(f, player, shrine, healingShrine->UsagePolicy) == false)
                return ContextInteractionState.AlreadyUsed;

            // Checked last - a player who's both already used it AND currently full should see
            // "already used" (the more permanent reason), not "full health" (which would flip back
            // to Available the instant they take any damage, misleadingly implying they could then
            // use it again this Break).
            if (f.Unsafe.TryGetPointer<Health>(player, out var health) == true && health->CurrentHealth >= health->MaxHealth)
                return ContextInteractionState.NotNeeded;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed - SkillSystem's own redirect gate lets both Available AND NotNeeded
        // through (see its own comment), so this has to handle NotNeeded explicitly rather than
        // just falling through the plain Available check every other rejection reason uses.
        // Re-validates in full (never trusts the View/target resolution alone, same reasoning
        // CursedRiftUtility.TryBeginInteraction documents), then heals and marks used in the SAME
        // tick - no persistent interaction state to track between now and then.
        public static void TryInteract(Frame f, EntityRef player, EntityRef shrine)
        {
            ContextInteractionState state = ResolveInteractionState(f, player, shrine);

            if (state == ContextInteractionState.NotNeeded)
            {
                // A real, deliberate press that does nothing (already at full Health) - fires a
                // View-only event so InteractionPromptWidget can show a ToastManager popup, since
                // the Base Skill icon simply not swapping is easy to miss on an actual button press.
                f.Events.ContextInteractionRejected(player, shrine);
                return;
            }

            if (state != ContextInteractionState.Available)
                return;

            if (f.Unsafe.TryGetPointer<HealingShrine>(shrine, out var healingShrine) == false)
                return;

            // Owner == the healed player itself - Shield is never touched (HealUtility only ever
            // touches Health), matching the spec's own "do not restore Shield" rule for free.
            HealUtility.ApplyHeal(f, player, player, healingShrine->HealPercent);
            PoiUsageUtility.MarkUsed(f, player, shrine, healingShrine->UsagePolicy);

            Log.Debug($"[HealingShrine] {player} interacted with {shrine} - healed {healingShrine->HealPercent.AsFloat:P0} of Max Health");
        }
    }
}
