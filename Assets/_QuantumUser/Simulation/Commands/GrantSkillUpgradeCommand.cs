namespace Quantum
{
    using Photon.Deterministic;

    // Lets a client grant an upgrade to its own player's skill slot outside the normal polled-input
    // flow - the same SkillSystem.AddUpgrade a future level-up/pickup choice screen would call.
    // Upgrades lives on simulation state, so only a command (replicated like input, executed on the
    // same tick by every client) can mutate it and stay deterministic - a direct call from the View
    // would only ever run locally. Currently only sent by the debug upgrade tester (View/Managers/
    // SkillUpgradeDebugTrigger.cs); reused as-is once a real level-up screen exists.
    public unsafe class GrantSkillUpgradeCommand : DeterministicCommand
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
