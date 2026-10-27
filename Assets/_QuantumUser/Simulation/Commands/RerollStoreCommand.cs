namespace Quantum
{
    using Photon.Deterministic;

    // Sent from the Store's Choice Window Reroll button - spends one shared CharacterStats.RerollQuantity
    // charge to restock the Store's rolled weapon + food offers (shared inventory, so for the whole team).
    // No payload; the sender is resolved via PlayerLink. See StoreUtility.Reroll.
    public unsafe class RerollStoreCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
