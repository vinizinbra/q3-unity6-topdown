namespace Quantum
{
    using UnityEngine;

    // Base for the "Passive Upgrade" level-up pool kind - see docs/level-up-upgrades.md. Icon/
    // DisplayName/Rarity come from UpgradeData; this adds an abstract Apply, same shape as
    // GlobalUpgradeData.Apply(Frame, EntityRef) - each concrete effect (e.g. a hero's own passive
    // ascension) is its own subtype rather than a switch here, and reads whatever hero-specific
    // component it needs off the entity itself.
    public abstract partial class PassiveUpgradeData : UpgradeData
    {
        [TextArea(2, 4)]
        public string Description;

        public abstract void Apply(Frame f, EntityRef entity);

        public override string GetDescription() => Description;
    }
}
