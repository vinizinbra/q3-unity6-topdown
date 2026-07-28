namespace Quantum
{
    using Photon.Deterministic;

    // Domain 1 (Survival Progression) - advances the survival clock/phase every tick. Owns pacing
    // only; never spawns anything itself (see CombatDirectorUtility for that).
    public static unsafe class SurvivalProgressionUtility
    {
        // Advances SurvivalTime/PhaseTimer and, once the current phase's Duration elapses,
        // CurrentPhaseIndex - holding forever once the last authored phase is reached (its
        // Duration is simply never checked again). Returns the phase now in effect this tick.
        public static SurvivalPhase Tick(Frame f, SurvivalConfig config)
        {
            f.Global->SurvivalTime += f.DeltaTime;
            f.Global->PhaseTimer += f.DeltaTime;

            SurvivalPhase currentPhase = config.Phases[f.Global->CurrentPhaseIndex];
            bool isLastPhase = f.Global->CurrentPhaseIndex >= config.Phases.Length - 1;

            if (isLastPhase == false && f.Global->PhaseTimer >= currentPhase.Duration)
            {
                f.Global->CurrentPhaseIndex++;
                f.Global->PhaseTimer = FP._0;
                currentPhase = config.Phases[f.Global->CurrentPhaseIndex];
                Log.Debug($"[Director] advanced to phase {f.Global->CurrentPhaseIndex}");
            }

            return currentPhase;
        }
    }
}
