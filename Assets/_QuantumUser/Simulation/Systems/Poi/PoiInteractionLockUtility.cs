namespace Quantum
{
    // Shared "is this player's movement/weapon/Base-Skill input locked to an open POI Choice UI
    // right now" check - generalizes what used to be CursedRiftUtility.IsInputLocked's own single
    // check into an OR across every POI session component that can claim a player's input (Cursed
    // Rift/Store/Blacksmith/Revive - see docs/breathing-poi.md/docs/store-blacksmith.md/
    // docs/revive.md). Read by WeaponSystem/SkillSystem and ContextInteractionSystem's own Busy
    // check, same call sites CursedRiftUtility.IsInputLocked used to serve alone. A future POI
    // session component just adds one more f.Has check here, nowhere else.
    //
    // NOTE: PlayerMovementProcessor.BeforeMove deliberately does NOT fold ReviveChannel into its
    // own use of this check the way the other call sites do - a reviver must keep moving at a
    // reduced (not zero) speed, so that call site special-cases ReviveChannel separately instead of
    // treating it as a full stop. See that method's own comment.
    public static class PoiInteractionLockUtility
    {
        public static bool IsInputLocked(Frame f, EntityRef entity)
        {
            return f.Has<CursedRiftInteraction>(entity) == true
                || f.Has<StoreInteraction>(entity) == true
                || f.Has<BlacksmithInteraction>(entity) == true
                || f.Has<ReviveChannel>(entity) == true;
        }
    }
}
