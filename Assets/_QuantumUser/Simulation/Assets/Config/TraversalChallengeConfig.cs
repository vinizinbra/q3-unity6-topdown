namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Shared Traversal Challenge tuning (see docs/traversal-challenge.md), referenced from
    // OverallGameConfig.TraversalChallengeConfig. The timer is authored here in plain seconds per difficulty
    // tier - not as a DifficultyConfig channel - since it's traversal tuning, not enemy difficulty, and
    // Nightmare is flat rather than compounding per level. TraversalChallengeUtility.TryActivate reads it;
    // the per-activator TraversalChallenge.Duration is now only the fallback when no config is assigned.
    public class TraversalChallengeConfig : AssetObject
    {
        [Header("Timer (seconds) per Difficulty")]
        public FP DurationEasy = 90;
        public FP DurationMedium = 60;
        public FP DurationHard = 45;

        [Tooltip("Every Nightmare level uses this - it doesn't shrink further per level.")]
        public FP DurationNightmare = 30;

        public FP GetDuration(DifficultyTier tier) => tier switch
        {
            DifficultyTier.Easy => DurationEasy,
            DifficultyTier.Hard => DurationHard,
            DifficultyTier.Nightmare => DurationNightmare,
            _ => DurationMedium,
        };
    }
}
