namespace Quantum
{
    using Photon.Deterministic;

    // WeaponWeight (docs/hero-mastery.md's "Initial Weight Behavior") is a general weapon property,
    // not a Hero Mastery concept - Hero Mastery (HeroMasteryUtility/WeaponWeightMasteryData) just
    // queries WeaponDataAsset.Weight the same way it used to query Family, and this move-speed effect
    // lives here instead, alongside it, so the two never duplicate each other's resolution logic.
    public static unsafe class WeaponWeightUtility
    {
        private static readonly FP LightMoveSpeedMultiplier = FP.FromString("1.05");
        private static readonly FP HeavyMoveSpeedMultiplier = FP.FromString("0.95");

        // Folded into the player's move speed by StatUtility.ResolveBaselineMoveSpeedMultiplier (read
        // by PlayerMovementProcessor and the Hero Info "Move Speed" row alike), next to the other
        // always-on contributors (CharacterStats/timed buffs/MutationModifier) - 1x (no effect) for Medium or for an owner with no equipped weapon.
        // A max-rank Weight Mastery with IgnoreMoveSpeedPenalty (Brute/Kai Heavy R3) cancels the
        // penalty of a matching weapon; bonuses are never cancelled.
        public static FP GetMoveSpeedMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return FP._1;

            var weight = f.FindAsset(weapon->WeaponData).Weight;
            FP multiplier;
            switch (weight)
            {
                case WeaponWeight.Light: multiplier = LightMoveSpeedMultiplier; break;
                case WeaponWeight.Heavy: multiplier = HeavyMoveSpeedMultiplier; break;
                default: return FP._1;
            }

            if (multiplier < FP._1
                && f.Unsafe.TryGetPointer<WeaponWeightMastery>(owner, out var mastery)
                && mastery->IgnoreMoveSpeedPenalty
                && mastery->Weight == weight)
                return FP._1;

            return multiplier;
        }
    }
}
