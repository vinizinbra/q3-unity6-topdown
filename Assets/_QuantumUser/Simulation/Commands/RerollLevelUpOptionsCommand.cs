namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks the reroll button on their own LevelUpChoice screen - no payload
    // needed (unlike SelectLevelUpUpgradeCommand's OptionIndex), the sender is identified via
    // PlayerLink the same way every other level-up command already is. See
    // LevelUpSystem.ProcessRerollCommands / LevelUpUtility.RerollOptionsFor, which spends one
    // CharacterStats.RerollQuantity charge to redraw every one of this entity's own rolled Options
    // in place.
    public unsafe class RerollLevelUpOptionsCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
