namespace Quantum
{
    using Photon.Deterministic;

    // Sent when a player clicks one of their own rolled LevelUpChoice.Options cards. Unlike
    // GrantWeaponPerkCommand/GrantSkillUpgradeCommand (which name the asset to grant directly), this
    // only carries an index - the actual Kind/AssetRef/Slot to grant is read back off the sender's
    // own already-rolled LevelUpChoice component (found via PlayerLink, same lookup WeaponSystem/
    // SkillSystem use for their own commands), so a client can never request an upgrade it was never
    // actually offered, and can never touch another player's pick. See LevelUpSystem/LevelUpUtility.
    public unsafe class SelectLevelUpUpgradeCommand : DeterministicCommand
    {
        public byte OptionIndex;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref OptionIndex);
        }
    }
}
