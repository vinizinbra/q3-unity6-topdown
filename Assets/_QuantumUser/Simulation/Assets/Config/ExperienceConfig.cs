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

        // Flat difficulty knob on top of the curve - multiplies RequiredExperience.Evaluate's
        // result before the co-op XpRequirement multiplier is applied (see ExperienceUtility.
        // GetRequiredExperience). Lets XP be retuned without re-baking curve keyframes; 1 = curve
        // as authored.
        public FP DifficultyMultiplier = 1;

        // Base collection radius for an ExpOrb, multiplied by the collecting character's own
        // CharacterStats.PickupRangeMultiplier - see CurrencyOrbSystem.
        public FP PickupRadius = 1;

        // How long an uncollected orb lingers before DestroyAfterTime removes it.
        public FP OrbLifetime = 30;

        // Flat +damage-per-DISPLAYED-level bonus, additive (not compounding) - a level-1 player
        // (Global.Level == 0, see ExperienceUtility.Grant's own comment on Level vs. display level)
        // already carries +DamageBonusPerLevel, same as every other displayed-level-1 stat. Applied
        // to every damage source (see DamageUtility.ResolveOutgoingDamage) so it scales the whole
        // co-op run's power with the one shared Level rather than any per-weapon mechanic.
        public FP DamageBonusPerLevel = FP.FromString("0.02");
    }
}
