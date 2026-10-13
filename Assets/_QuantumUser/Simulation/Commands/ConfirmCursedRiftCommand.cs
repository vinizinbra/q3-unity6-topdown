namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks a "SACRIFICE" card on their own rolled CursedRiftInteraction - the sim
    // already knows the rolled sacrifice(s) and mutation reward from TryBeginInteraction. OptionIndex
    // picks which sacrifice to pay: 0 = Sacrifice, 1 = AltSacrifice (only rolled with Sacrifice Rift L2,
    // meta progression). Applies the cost and grants the mutation in the same tick - see
    // CursedRiftUtility.Confirm.
    public unsafe class ConfirmCursedRiftCommand : DeterministicCommand
    {
        public byte OptionIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OptionIndex);
        }
    }
}
