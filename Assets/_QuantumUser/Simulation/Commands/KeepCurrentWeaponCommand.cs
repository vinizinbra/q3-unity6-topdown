namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks the separate "Keep Current" button on a Choose-Weapon screen -
    // distinct from SelectLevelUpUpgradeCommand, since this is NOT one of the 3 rolled
    // LevelUpChoice.Options (all 3 stay real rolled weapons - see
    // LevelUpUtility.RollChooseWeaponOptionsFor). No payload needed, same as
    // RerollLevelUpOptionsCommand - the sender is identified via PlayerLink, same as every other
    // level-up command. See LevelUpSystem.ProcessKeepCurrentCommands /
    // LevelUpUtility.ConfirmKeepCurrent.
    public unsafe class KeepCurrentWeaponCommand : DeterministicCommand
    {
        public override void Serialize(BitStream stream)
        {
        }
    }
}
