namespace Quantum
{
    using Photon.Deterministic;

    // Escalating Survival intensity - every combat phase starts manageable and becomes increasingly
    // chaotic, then resets when the next one begins. RUN-SCOPE.
    //
    // Renamed from "Pressure Cooker" to free that name for a personal, per-player damage-ramp
    // Epic. This one ramps the ENCOUNTER (how fast enemies arrive), which "escalation" describes
    // more precisely than the old name did.
    //
    // The ramp is derived from the phase's own normalized progress (PhaseTimer / Duration) rather
    // than any timer of its own - see EncounterModifierUtility.ResolvePhaseRamp. That keeps it fully
    // deterministic, and means the reset is free: SurvivalProgressionUtility.Tick already zeroes
    // PhaseTimer on every phase transition, so nothing here has to notice a phase ending.
    //
    // Breathing and Boss phases are excluded by that same helper: a Break has no spawning to
    // escalate, and a boss encounter stops Director pulses entirely.
    public unsafe class EscalationMutationData : RiftMutationData
    {
        public FP EndOfPhaseDensityBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            // Take-the-stronger rather than additive - the field describes where the ramp ENDS, so
            // two sources summing into a runaway multiplier would be meaningless.
            f.Global->EscalationEndBonus = FPMath.Max(f.Global->EscalationEndBonus, EndOfPhaseDensityBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (FP._1 + EndOfPhaseDensityBonus).AsFloat
        };
    }
}
