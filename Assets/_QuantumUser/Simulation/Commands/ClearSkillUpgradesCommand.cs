namespace Quantum
{
    using Photon.Deterministic;

    // Debug-only "remove everything at once" counterpart to GrantSkillUpgradeCommand/
    // RemoveSkillUpgradeCommand - clears every upgrade from one of its own player's skill slots (see
    // SkillSystem.ClearUpgrades). Currently only sent by the debug upgrade tester
    // (View/Managers/SkillUpgradeDebugTrigger.cs).
    public unsafe class ClearSkillUpgradesCommand : DeterministicCommand
    {
        public SkillSlotId Slot;

        public override void Serialize(BitStream stream)
        {
            byte slot = (byte)Slot;
            stream.Serialize(ref slot);
            Slot = (SkillSlotId)slot;
        }
    }
}
