namespace Quantum
{
    // See Poi.qtn's own comment for the overall design. This is the ONLY place PoiAvailability is
    // interpreted against Global.CurrentState (and, for Breathing, Global.BreathingAreaSecured too)
    // - a POI system just calls IsAvailable with its own component's field, never branches on
    // GameState/BreathingAreaSecured itself.
    public static class PoiAvailabilityUtility
    {
        public static unsafe bool IsAvailable(Frame f, PoiAvailability availability)
        {
            switch (f.Global->CurrentState)
            {
                case GameState.Survival: return availability.AvailableInCombat;

                // Also gated on Global.BreathingAreaSecured - the area isn't actually "secured"
                // just because the phase boundary was crossed (see SurvivalProgressionUtility.
                // IsEncounterCleared/docs/run-phase.md); a Healing Shrine/Cursed Rift/Store/
                // Blacksmith shouldn't be usable while an enemy (usually an Economy.Persistent one
                // still fighting - non-persistent ones are force-cleared the same tick) is still
                // alive.
                case GameState.Breathing: return availability.AvailableInBreathing && f.Global->BreathingAreaSecured;

                default: return false; // Lobby/Upgrade/Event/Boss - never available
            }
        }
    }
}
