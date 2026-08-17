namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player presses the Cancel button on a Blacksmith pick screen - no payload
    // needed. Free (no PoiUsage marked) - see BlacksmithUtility.Cancel.
    public unsafe class CancelBlacksmithCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
