namespace Quantum
{
    using Photon.Deterministic;

    public class TalentsConfig : AssetObject
    {
        // One shared step for every leveling talent (PlayerDamageLevel, PlayerCooldownLevel,
        // etc.) - not per-stat, not per-level-index. Level N bonus = N * PercentPerLevel. See
        // TalentUtility.ApplyPerPlayerTalents.
        public FP PercentPerLevel = 5;
    }
}
