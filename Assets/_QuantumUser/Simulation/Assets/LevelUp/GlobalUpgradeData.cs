namespace Quantum
{
    // Base for the "Global Upgrade" level-up pool kind - see docs/level-up-upgrades.md. Icon/
    // DisplayName/Rarity come from UpgradeData; this adds an abstract Apply, same shape as
    // WeaponPerkData.Apply(Frame, Weapon*) - each concrete effect (e.g. HealthRegenUpgradeData) is
    // its own subtype rather than a switch here. View-only Description lives in the companion
    // GlobalUpgradeData.View.cs partial, same split as WeaponPerkData/WeaponPerkData.View.cs.
    public abstract partial class GlobalUpgradeData : UpgradeData
    {
        public abstract void Apply(Frame f, EntityRef entity);

        public override string GetDescription() => GetFormattedDescription();
    }
}
