namespace Quantum
{
    using Photon.Deterministic;

    // Global balance tuning for the Haste status effect - one shared knob for "how much attack
    // speed and for how long", instead of authored separately on every HasteEffectData asset that
    // grants it. Referenced via RuntimeConfig.HasteConfig.
    public class HasteConfig : AssetObject
    {
        public FP Duration = 5;
        public FP AttackSpeedMultiplier = FP.FromString("1.5");
    }
}
