namespace Quantum
{
    using QuantumUser.View;

    // Per-VIEWER refinement of the shared PoiActivation.State. The sim resolves Active while ANY
    // connected player can still use the POI (a deterministic, every-client-agrees-on fact - see
    // PoiActivationUtility), but presentation should be local: if THIS client's own local
    // player(s) are on cooldown / already used it, the POI reads Expired here even while a
    // teammate could still use it. View-only - never feeds back into the simulation.
    public static unsafe class PoiLocalViewState
    {
        public static PoiViewState Resolve(Frame f, EntityRef poi, PoiViewState sharedState)
        {
            // Inactive/Expired are already true for every viewer - only Active can be narrowed.
            if (sharedState != PoiViewState.Active || MyLocalPlayer.Instance == null)
                return sharedState;

            PoiUsagePolicy policy = ResolveUsagePolicy(f, poi);

            if (policy == PoiUsagePolicy.Reusable)
                return sharedState;

            var slots = MyLocalPlayer.Instance.Slots;
            bool anyLocalPlayer = false;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsSet == false || f.Exists(slots[i].EntityRef) == false)
                    continue;

                anyLocalPlayer = true;

                if (PoiUsageUtility.CanUse(f, slots[i].EntityRef, poi, policy) == true)
                    return PoiViewState.Active;
            }

            // No local player resolved yet (spectating/joining) - fall back to the shared state.
            return anyLocalPlayer ? PoiViewState.Expired : sharedState;
        }

        // UsagePolicy lives on each POI kind's own component (see Poi.qtn) - Store has none
        // (always Reusable, see PoiActivationSystem).
        public static PoiUsagePolicy ResolveUsagePolicy(Frame f, EntityRef poi)
        {
            if (f.Unsafe.TryGetPointer<HealingShrine>(poi, out var shrine))
                return shrine->UsagePolicy;

            if (f.Unsafe.TryGetPointer<CursedRift>(poi, out var rift))
                return rift->UsagePolicy;

            if (f.Unsafe.TryGetPointer<Blacksmith>(poi, out var forge))
                return forge->UsagePolicy;

            return PoiUsagePolicy.Reusable;
        }
    }
}
