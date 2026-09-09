namespace Quantum
{
    // Shared "is this player's movement/weapon/Base-Skill input locked to an open POI Choice UI
    // right now" check - generalizes what used to be CursedRiftUtility.IsInputLocked's own single
    // check into an OR across every POI session component that can claim a player's input (Cursed
    // Rift/Store/Blacksmith/Revive - see docs/breathing-poi.md/docs/store-blacksmith.md/
    // docs/revive.md). Read by WeaponSystem/SkillSystem and ContextInteractionSystem's own Busy
    // check, same call sites CursedRiftUtility.IsInputLocked used to serve alone.
    //
    // Also home to HasChoiceWindowOpen - the narrower "Cursed Rift/Store/Blacksmith only" OR
    // (Revive excluded, it's a channel, not a Choice Window), reused by RunPhaseUtility's Breathing
    // grace hold and the View's ChoiceWindowIndicatorUiWidget (party-strip indicator). A future POI
    // Choice Window just adds one more f.Has check to HasChoiceWindowOpen, nowhere else - every
    // other place already funnels through it.
    //
    // NOTE: PlayerMovementProcessor.BeforeMove deliberately does NOT fold ReviveChannel into its
    // own use of this check the way the other call sites do - a reviver must keep moving at a
    // reduced (not zero) speed, so that call site special-cases ReviveChannel separately instead of
    // treating it as a full stop. See that method's own comment.
    public static unsafe class PoiInteractionLockUtility
    {
        // The single OR across every "Choice Window" POI session component - Revive is deliberately
        // NOT included here (it's a channel, not a Choice Window; ChoiceWindowIndicatorUiWidget/
        // RunPhaseUtility.AnyConnectedPlayerHasChoiceWindowOpen only care about the latter). This is
        // the ONE place that list lives, read from both simulation (IsInputLocked below,
        // RunPhaseUtility's grace hold) and the View (ChoiceWindowIndicatorUiWidget's party-strip
        // indicator) - a future POI Choice Window just adds one more f.Has check here, nowhere else.
        public static bool HasChoiceWindowOpen(Frame f, EntityRef entity)
        {
            return f.Has<CursedRiftInteraction>(entity) == true
                || f.Has<StoreInteraction>(entity) == true
                || f.Has<BlacksmithInteraction>(entity) == true;
        }

        public static bool IsInputLocked(Frame f, EntityRef entity)
        {
            if (HasChoiceWindowOpen(f, entity) == true || f.Has<ReviveChannel>(entity) == true)
                return true;

            // A Breathing grace period is running (RunPhaseUtility.TickBreathingGraceHold - see
            // GameState.qtn's own BreathingGraceActive comment) and this entity does NOT itself have
            // a Choice Window open - a bystander, fully locked via this same shared check every call
            // site above already uses, so they can't wander off while a teammate finishes. A player
            // who DOES have a window open already returned true above.
            return f.Global->BreathingGraceActive == true && HasChoiceWindowOpen(f, entity) == false;
        }
    }
}
