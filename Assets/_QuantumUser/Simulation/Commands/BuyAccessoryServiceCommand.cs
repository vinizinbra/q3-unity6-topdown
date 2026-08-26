namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks the Merchant's guaranteed Accessory Repair/Replacement card - no
    // payload needed for the same reason BuyStoreWeaponLevelCommand has none: there is only ever one
    // such service, and WHICH service it is (repair vs replacement) plus its price are both derived
    // simulation-side from the buyer's own durability, never sent from the View. See
    // AccessoryServiceUtility.TryPurchaseService.
    public unsafe class BuyAccessoryServiceCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
