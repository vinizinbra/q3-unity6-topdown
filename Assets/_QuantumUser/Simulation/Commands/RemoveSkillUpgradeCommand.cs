namespace Quantum
{
    using Photon.Deterministic;

    // Debug counterpart to GrantSkillUpgradeCommand - removes a previously-granted upgrade from its
    // own player's skill slot (see SkillSystem.RemoveUpgrade). Same "must be a command, not a direct
    // View call" reasoning as the grant command. Currently only sent by the debug upgrade tester
    // (View/Managers/SkillUpgradeDebugTrigger.cs).
    public unsafe class RemoveSkillUpgradeCommand : DeterministicCommand
    {
        public AssetRef<SkillActionData> Upgrade;
        public SkillSlotId Slot;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Upgrade);

            byte slot = (byte)Slot;
            stream.Serialize(ref slot);
            Slot = (SkillSlotId)slot;
        }
    }
}
