namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks one of their own rolled CursedRiftInteraction.SacrificeChoices
    // cards - same "index only, sim re-resolves the rest off the sender's own already-rolled
    // state" shape as SelectLevelUpUpgradeCommand. Commits immediately (applies the sacrifice's
    // cost) and rolls the interaction into SelectingMutation - see CursedRiftUtility.SelectSacrifice.
    public unsafe class SelectSacrificeCommand : DeterministicCommand
    {
        public byte OptionIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OptionIndex);
        }
    }
}
