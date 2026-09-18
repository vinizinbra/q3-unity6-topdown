namespace Quantum
{
    // See Poi.qtn's own comment for the overall design. This is the ONLY place PoiAvailability is
    // interpreted against Global.CurrentState - a POI system just calls IsAvailable with its own
    // component's field, never branches on GameState itself.
    public static class PoiAvailabilityUtility
    {
        public static unsafe bool IsAvailable(Frame f, PoiAvailability availability)
        {
            switch (f.Global->CurrentState)
            {
                case GameState.Survival: return availability.AvailableInCombat;

                // No separate BreathingAreaSecured check needed here anymore - GameState.Breathing
                // itself now only ever starts once the area is secured (see CombatDirectorSystem.
                // ResolveDesiredState), so a Healing Shrine/Cursed Rift/Store/Blacksmith being
                // reachable at all already implies enemies are cleared. The "phase boundary crossed
                // but not yet secured" window reads as plain Survival instead (never available), not
                // Breathing.
                case GameState.Breathing: return availability.AvailableInBreathing;

                default: return false; // Lobby/Upgrade/Event/Boss/TeamChallenge/TraversalChallenge - never available
            }
        }
    }
}
