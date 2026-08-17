namespace Quantum
{
    using UnityEngine;

    // Base for the "Rift Mutation" level-up pool kind - see docs/rift-mutations.md. Icon/
    // DisplayName come from UpgradeData; this adds an abstract Apply, same shape as
    // GlobalUpgradeData.Apply(Frame, EntityRef) - each concrete effect (e.g. GlassCoreMutationData)
    // is its own subtype rather than a switch here. View-only Description lives in the companion
    // RiftMutationData.View.cs partial, same split as GlobalUpgradeData/GlobalUpgradeData.View.cs.
    //
    // Rarity is its own field here (not shared - see UpgradeData's own comment), since only
    // WeaponPerkData/RiftMutationData still have a rarity axis - decides this mutation's weight via
    // LevelUpConfig.GetWeight.
    //
    // Deliberately no MaxPicks field, unlike GlobalUpgradeData - every Rift Mutation is
    // non-stackable, a pool-wide rule rather than an opt-in per-asset cap. See RiftMutationPicks
    // (LevelUp.qtn) and RiftMutationUtility.Grant/IsAlreadyPicked.
    public abstract partial class RiftMutationData : UpgradeData
    {
        [Tooltip("How likely this mutation is to come up in a level-up roll - see LevelUpConfig.GetWeight.")]
        public UpgradeRarity Rarity = UpgradeRarity.Common;

        public abstract void Apply(Frame f, EntityRef entity);

        public override string GetDescription() => GetFormattedDescription();
    }
}
