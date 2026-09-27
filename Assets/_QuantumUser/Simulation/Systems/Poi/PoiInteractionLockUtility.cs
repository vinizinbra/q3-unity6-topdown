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
            // Deliberately does NOT read Global.BreathingGraceActive - a Breathing grace hold
            // (RunPhaseUtility.TickBreathingGraceHold) only delays the phase transition; teammates
            // without a window of their own keep full control while they wait (confirmed with the
            // user - locking them read as a random freeze in online co-op).
            return HasChoiceWindowOpen(f, entity) == true || f.Has<ReviveChannel>(entity) == true;
        }
    }
}
