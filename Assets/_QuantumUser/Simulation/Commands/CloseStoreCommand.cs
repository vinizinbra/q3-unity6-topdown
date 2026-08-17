namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player presses the Store window's Close button - no payload needed. See
    // StoreUtility.Close.
    public unsafe class CloseStoreCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
