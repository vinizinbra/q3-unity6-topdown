namespace Quantum
{
    using Photon.Deterministic;

    // Sent from the Blacksmith's Choice Window Reroll button - redraws this player's 3 perk options once
    // per Break (Gunsmith L4, meta progression). No payload; the sender is resolved via PlayerLink like
    // every other Blacksmith command. See BlacksmithUtility.Reroll.
    public unsafe class RerollBlacksmithCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
