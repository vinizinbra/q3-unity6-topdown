namespace Quantum
{
    using System.Collections.Generic;
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
    // (LevelUp.qtn) and RiftMutationUtility.Grant/IsBlocked.
    public abstract partial class RiftMutationData : UpgradeData
    {
        [Tooltip("How likely this mutation is to come up in a level-up roll - see LevelUpConfig.GetWeight.")]
        public UpgradeRarity Rarity = UpgradeRarity.Common;

        [Tooltip("Player = affects only whoever picks it. Run = changes shared simulation state (Frame.Global) and is applied exactly ONCE per run no matter how many players are offered it - see MutationScope and RunMutations.qtn.")]
        public MutationScope Scope = MutationScope.Player;

        [Tooltip("Mutations that can never be owned alongside this one. Checked SYMMETRICALLY by RiftMutationUtility.IsBlocked, so a mutually-exclusive pair only needs authoring on ONE of its two sides. Filtered out of every offer (level-up, Chest and Cursed Rift alike) once the other half is owned.")]
        public List<AssetRef<RiftMutationData>> IncompatibleWith = new List<AssetRef<RiftMutationData>>();

        // Generic prerequisite gate, same shape as PassiveUpgradeData/SkillActionData/
        // GlobalUpgradeData - default true, so every existing mutation is unaffected.
        //
        // Checked inside RiftMutationUtility.IsBlocked rather than in the collectors, which means a
        // mutation gated here is filtered out of level-ups, Chests, Cursed Rift rewards AND the
        // debug-grant path at once, since Grant re-checks IsBlocked.
        //
        // The established idiom for "does this player have capability X" in this codebase is a
        // marker component tested with f.Has<T> (see FlashpointPassiveUpgradeData checking
        // CanApplyBurn), not a string tag - the Accessory-dependent mutations follow it via
        // AccessoryGuardUtility.IsAvailable.
        public virtual bool IsEligible(Frame f, EntityRef entity) => true;

        public abstract void Apply(Frame f, EntityRef entity);

        public override string GetDescription() => GetFormattedDescription();
    }
}
