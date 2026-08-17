namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks one of their own rolled BlacksmithInteraction.PerkChoices cards -
    // same "index only, sim re-resolves the rest off the sender's own already-rolled state" shape
    // as SelectSacrificeCommand. See BlacksmithUtility.SelectPerk.
    public unsafe class SelectBlacksmithPerkCommand : DeterministicCommand
    {
        public byte OptionIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OptionIndex);
        }
    }
}
