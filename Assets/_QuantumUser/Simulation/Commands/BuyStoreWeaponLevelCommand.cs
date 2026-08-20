namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks the Store's guaranteed "Increase Weapon Level" offer - no payload
    // needed, there's only ever one such offer per Store. See StoreUtility.BuyWeaponLevelUp, which
    // re-validates affordability/once-per-Break server-side rather than trusting the click alone.
    public unsafe class BuyStoreWeaponLevelCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
