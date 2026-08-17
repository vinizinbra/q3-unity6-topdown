namespace Quantum
{
    // Base for the "Global Upgrade" level-up pool kind - see docs/level-up-upgrades.md. Icon/
    // DisplayName come from UpgradeData (no Rarity - see UpgradeData's own comment); this adds an
    // abstract Apply, same shape as WeaponPerkData.Apply(Frame, Weapon*) - each concrete effect
    // (e.g. HealthRegenUpgradeData) is its own subtype rather than a switch here. View-only
    // Description lives in the companion
    // GlobalUpgradeData.View.cs partial, same split as WeaponPerkData/WeaponPerkData.View.cs.
    public abstract partial class GlobalUpgradeData : UpgradeData
    {
        // 0 (the default) means unlimited - most Global Upgrades are small flat increments meant to
        // stack indefinitely (see docs/global-upgrades.md). Set > 0 on an upgrade whose effect
        // shouldn't be picked forever (e.g. Dash Charge) - LevelUpUtility.CollectGlobalCandidates
        // stops offering it once GlobalUpgradeUtility.GetPickCount reaches this for the rolling
        // entity, and GlobalUpgradeUtility.Grant is what actually records each pick.
        public byte MaxPicks = 0;

        public abstract void Apply(Frame f, EntityRef entity);

        public override string GetDescription() => GetFormattedDescription();
    }
}
