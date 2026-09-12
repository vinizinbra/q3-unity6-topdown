namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks the single "SACRIFICE" button on their own rolled
    // CursedRiftInteraction - no payload needed, the sim already knows both the rolled sacrifice
    // and the rolled mutation reward from TryBeginInteraction. Applies the sacrifice's cost and
    // grants the mutation in the same tick - see CursedRiftUtility.Confirm.
    public unsafe class ConfirmCursedRiftCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
