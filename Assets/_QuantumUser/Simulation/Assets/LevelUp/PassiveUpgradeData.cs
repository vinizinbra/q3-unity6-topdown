namespace Quantum
{
    using UnityEngine;

    // Plumbing-only stub for the "Passive Upgrade" level-up pool kind - see
    // docs/level-up-upgrades.md. No gameplay effect exists yet (see PassiveUpgradeUtility.Grant);
    // Icon/DisplayName/Rarity come from UpgradeData, this only adds the player-facing Description
    // card text.
    public class PassiveUpgradeData : UpgradeData
    {
        [TextArea(2, 4)]
        public string Description;

        public override string GetDescription() => Description;
    }
}
