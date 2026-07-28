namespace Quantum
{
    using UnityEngine;

    // Shared display metadata + rarity for anything offerable as a level-up upgrade card -
    // WeaponPerkData, SkillActionData, GlobalUpgradeData and PassiveUpgradeData all derive from
    // this instead of each declaring their own Icon/DisplayName/Rarity independently. See
    // LevelUpPoolKind/LevelUpOption (Assets/_QuantumUser/Simulation/QTN/LevelUp.qtn),
    // UpgradeCardWidget and docs/level-up-upgrades.md.
    //
    // Deliberately no Description field here - GetDescription() is abstract instead, since a
    // subtype's real player-facing text isn't always a plain authored field (SkillActionData
    // overrides it to return its own live-templated GetFormattedDescription() rather than a static
    // string). Icon/DisplayName/Rarity ARE plain authored fields since every kind wants the exact
    // same thing for those.
    public abstract class UpgradeData : AssetObject
    {
        [Tooltip("Shown wherever this upgrade is listed as a level-up choice.")]
        public Sprite Icon;

        [Tooltip("Player-facing upgrade name shown on a level-up choice card. The asset name is not used as a fallback.")]
        public string DisplayName;

        [Tooltip("How likely this upgrade is to come up in a level-up roll - see LevelUpConfig.GetWeight.")]
        public UpgradeRarity Rarity = UpgradeRarity.Common;

        public abstract string GetDescription();
    }
}
