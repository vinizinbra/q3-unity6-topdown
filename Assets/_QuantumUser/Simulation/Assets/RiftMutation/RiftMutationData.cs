namespace Quantum
{
    // Base for the "Rift Mutation" level-up pool kind - see docs/rift-mutations.md. Icon/
    // DisplayName/Rarity come from UpgradeData; this adds an abstract Apply, same shape as
    // GlobalUpgradeData.Apply(Frame, EntityRef) - each concrete effect (e.g. GlassCoreMutationData)
    // is its own subtype rather than a switch here. View-only Description lives in the companion
    // RiftMutationData.View.cs partial, same split as GlobalUpgradeData/GlobalUpgradeData.View.cs.
    //
    // Deliberately no MaxPicks field, unlike GlobalUpgradeData - every Rift Mutation is
    // non-stackable, a pool-wide rule rather than an opt-in per-asset cap. See RiftMutationPicks
    // (LevelUp.qtn) and RiftMutationUtility.Grant/IsAlreadyPicked.
    public abstract partial class RiftMutationData : UpgradeData
    {
        public abstract void Apply(Frame f, EntityRef entity);

        public override string GetDescription() => GetFormattedDescription();
    }
}
