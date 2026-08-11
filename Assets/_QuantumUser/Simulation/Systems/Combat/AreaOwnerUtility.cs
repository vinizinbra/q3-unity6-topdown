namespace Quantum
{
    // Shared read side of AreaOwner - extracted from AreaDamageSystem's own former private
    // ResolveOwner so AlternatingAreaSystem's bonus-beat code (Main Stage's opening/closing beats)
    // can resolve the exact same owner/source/element an area's regular tick already would, without
    // duplicating the lookup.
    public static unsafe class AreaOwnerUtility
    {
        // Optional rather than a required lookup so a hand-placed level hazard with no AreaOwner can
        // still resolve to a sensible "nobody" default - see AreaOwner.qtn.
        public static void Resolve(Frame f, EntityRef entity, out EntityRef owner, out DamageSource source,
            out ElementType element)
        {
            if (f.Unsafe.TryGetPointer<AreaOwner>(entity, out var areaOwner) == true)
            {
                owner = areaOwner->Owner;
                source = areaOwner->Source;
                element = areaOwner->Element;
                return;
            }

            owner = EntityRef.None;
            source = DamageSource.None;
            element = ElementType.Neutral;
        }
    }
}
