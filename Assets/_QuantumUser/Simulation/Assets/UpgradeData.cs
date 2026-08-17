namespace Quantum
{
    using UnityEngine;

    // Shared display metadata for anything offerable as a level-up upgrade card - WeaponPerkData,
    // SkillActionData, GlobalUpgradeData, PassiveUpgradeData and RiftMutationData all derive from
    // this instead of each declaring their own Icon/DisplayName independently. See
    // LevelUpPoolKind/LevelUpOption (Assets/_QuantumUser/Simulation/QTN/LevelUp.qtn),
    // UpgradeCardWidget and docs/level-up-upgrades.md.
    //
    // Deliberately no Description field here - GetDescription() is abstract instead, since a
    // subtype's real player-facing text isn't always a plain authored field (SkillActionData
    // overrides it to return its own live-templated GetFormattedDescription() rather than a static
    // string). Icon/DisplayName ARE plain authored fields since every kind wants the exact same
    // thing for those.
    //
    // No Rarity here - only WeaponPerkData/RiftMutationData still have one (their own field, not
    // shared) and weight their level-up rolls by it; SkillActionData/GlobalUpgradeData/
    // PassiveUpgradeData draw at a flat LevelUpConfig.CommonWeight instead - see
    // LevelUpUtility.ResolveWeight.
    public abstract class UpgradeData : AssetObject
    {
        [Tooltip("Shown wherever this upgrade is listed as a level-up choice.")]
        public Sprite Icon;

        [Tooltip("Player-facing upgrade name shown on a level-up choice card. The asset name is not used as a fallback.")]
        public string DisplayName;

        public abstract string GetDescription();
    }
}
