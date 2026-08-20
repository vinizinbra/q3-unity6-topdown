namespace Quantum
{
    // The interaction/channel half of reviving a TEAMMATE (see docs/revive.md) - mirrors
    // HealingShrineUtility/CursedRiftUtility's own shape. PlayerLifeStateUtility owns the actual
    // Alive/Downed/KO transitions and the completion heal/invuln; this owns starting, validating and
    // cancelling a ReviveChannel, PLUS the entirely separate self-revive path (TryPerformSelfRevive -
    // a deliberate single press/confirm via SelfReviveCommand, never a hold/channel). Revive has no
    // PoiAvailability/PoiUsagePolicy concept (unlike every other Interactable kind) - it must work
    // identically in Combat and Breathing. Both paths here only ever target/apply to a DOWNED
    // player - KO has no revive path at all anymore (teammate hold or self-revive alike), it's a
    // dead end until Global.BreathingAreaSecured auto-revives everyone still incapacitated
    // (PlayerLifeStateUtility.EnterKO/ReviveAllIncapacitated) - confirmed with the user.
    public static unsafe class ReviveUtility
    {
        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution, self-skip and kind-tier priority already happened there). A KO'd target
        // shouldn't actually reach here at all - EnterKO removes its own Interactable, so
        // ContextInteractionSystem's scan never surfaces it as a candidate in the first place - but
        // this checks State != Downed rather than just != Alive anyway, same "never trust it"
        // discipline every other resolver here already follows.
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var lifeState) == false
                || lifeState->State != PlayerLifeStateKind.Downed)
            {
                return ContextInteractionState.None;
            }

            if (lifeState->ReviveHolder != EntityRef.None && lifeState->ReviveHolder != player)
                return ContextInteractionState.Occupied;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem's redirect on a fresh Hero Skill press while ContextInteraction.
        // State == Available and ActiveKind == Revive. Re-validates in full, never trusts the
        // resolved ContextInteraction alone - same reasoning every other POI's own
        // TryBeginInteraction/TryInteract already documents.
        public static void TryBeginInteraction(Frame f, EntityRef player, EntityRef target)
        {
            if (f.Has<ReviveChannel>(player) == true)
                return;

            if (ResolveInteractionState(f, player, target) != ContextInteractionState.Available)
                return;

            if (f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var lifeState) == false)
                return;

            f.AddOrGet<ReviveChannel>(player, out var channel);
            channel->Target = target;

            lifeState->ReviveHolder = player;

            Log.Debug($"[Revive] {player} began reviving {target}");
        }

        // Processed from PlayerLifeStateSystem on a received SelfReviveCommand - a deliberate single
        // press/confirm (see docs/revive.md), unlike a teammate revive's own hold/channel. Only
        // works while Downed - KO has no self-revive path anymore, same as it has no teammate-hold
        // path (see this class's own header comment); a KO'd player's own charges, if any remain
        // unspent, simply sit unused until Global.BreathingAreaSecured auto-revives them for free.
        // Fully re-validated here, never trusted from the View alone. No-op (not an error) if the
        // command arrives for a player who's Alive, KO, out of charges, or already revived this
        // same tick by a teammate - all legitimate races, not bugs.
        public static void TryPerformSelfRevive(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<PlayerLifeState>(player, out var lifeState) == false || lifeState->State != PlayerLifeStateKind.Downed)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false || stats->SelfReviveCharges == 0)
                return;

            stats->SelfReviveCharges--;
            PlayerLifeStateUtility.Revive(f, player, player);

            Log.Debug($"[Revive] {player} self-revived");
        }

        // Called on any cancel (release, out of range, target invalidated, reviver themselves
        // incapacitated, or a fresh hit via ReviveDamageInterruptSystem). Deliberately does NOT
        // reset the target's own banked ReviveProgress anymore - PlayerLifeStateSystem decays it
        // back toward 0 gradually instead (ReviveConfig.ReviveProgressDecayRate), so an interrupted
        // hold leaves real partial credit for whoever resumes it rather than losing everything to a
        // single stray hit.
        public static void Cancel(Frame f, EntityRef reviver)
        {
            if (f.Unsafe.TryGetPointer<ReviveChannel>(reviver, out var channel) == false)
                return;

            EntityRef target = channel->Target;
            f.Remove<ReviveChannel>(reviver);

            if (f.Unsafe.TryGetPointer<PlayerLifeState>(target, out var lifeState) == true && lifeState->ReviveHolder == reviver)
            {
                lifeState->ReviveHolder = EntityRef.None;
            }

            Log.Debug($"[Revive] {reviver}'s revive channel on {target} cancelled");
        }
    }
}
