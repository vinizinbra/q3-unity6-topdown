namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player presses [BACK] (on the confirm sub-panel) or the main Cancel button (on
    // the sacrifice list) - no payload needed. CursedRiftUtility.Cancel decides which of the two
    // it means by the interaction's own current State, so one command covers both; a no-op once
    // payment is committed (irreversible past that point).
    public unsafe class CancelCursedRiftCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
