namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks one of their own eligible StoreInventory.WeaponOffers cards - see
    // StoreUtility.BuyWeapon, which re-validates OfferIndex/affordability/purchase-state server-side
    // rather than trusting this index alone.
    public unsafe class BuyStoreWeaponCommand : DeterministicCommand
    {
        public byte OfferIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OfferIndex);
        }
    }
}
