namespace Quantum
{
    using Photon.Deterministic;

    // Sent from the Cursed Rift's Choice Window Reroll button - spends one shared
    // CharacterStats.RerollQuantity charge to redraw this player's open sacrifice+mutation offer. No payload;
    // the sender is resolved via PlayerLink. See CursedRiftUtility.Reroll.
    public unsafe class RerollCursedRiftCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
