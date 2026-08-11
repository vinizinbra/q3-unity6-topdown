namespace Quantum
{
    using Photon.Deterministic;

    // Global tuning for the experience-drop mechanic - see ExpOrb.qtn, ExperienceUtility and
    // CurrencyOrbSystem. Referenced via RuntimeConfig.ExperienceConfig.
    public class ExperienceConfig : AssetObject
    {
        // X = Level, Y = cumulative TotalExperience required to reach that level - tweak the curve
        // directly in the Inspector rather than touching code (Quantum's FPAnimationCurve drawer
        // bakes a normal Unity AnimationCurve to deterministic FP samples on save). Evaluated by
        // ExperienceUtility.Grant.
        public FPAnimationCurve RequiredExperience;

        // Highest level RequiredExperience defines - Grant clamps here rather than reading past
        // the authored keyframes.
        public int MaxLevel = 50;

        // Base collection radius for an ExpOrb, multiplied by the collecting character's own
        // CharacterStats.PickupRangeMultiplier - see CurrencyOrbSystem.
        public FP PickupRadius = 1;

        // How long an uncollected orb lingers before DestroyAfterTime removes it.
        public FP OrbLifetime = 30;
    }
}
