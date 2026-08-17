namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks one of their own rolled CursedRiftInteraction.MutationChoices
    // cards - same "index only" shape as SelectLevelUpUpgradeCommand/SelectSacrificeCommand.
    // Grants the mutation and completes the interaction - see CursedRiftUtility.SelectMutation.
    public unsafe class SelectMutationCommand : DeterministicCommand
    {
        public byte OptionIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OptionIndex);
        }
    }
}
