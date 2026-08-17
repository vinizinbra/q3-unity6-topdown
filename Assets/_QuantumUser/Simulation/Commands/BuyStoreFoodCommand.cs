namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks one of the shared StoreInventory.FoodOffers cards - see
    // StoreUtility.BuyFood, which re-validates OfferIndex/affordability/purchase-state server-side
    // rather than trusting this index alone.
    public unsafe class BuyStoreFoodCommand : DeterministicCommand
    {
        public byte OfferIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OfferIndex);
        }
    }
}
