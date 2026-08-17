namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player presses "Skip Break" during a Breathing phase - casts (or re-casts,
    // idempotently) this player's own vote to end the CURRENT Break early. Once every currently-
    // connected player has voted this same Break, RunPhaseUtility.TryForceSkipBreathing ends it
    // immediately instead of waiting out its full authored Duration - see docs/run-phase.md.
    // Harmless if sent outside Breathing (or resent after already voting) - just (re)writes this
    // player's own BreathingSkipVote.VotedAtBreathingIndex, which only ever matters the next time
    // TryForceSkipBreathing actually checks it.
    public unsafe class SkipBreathingCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
