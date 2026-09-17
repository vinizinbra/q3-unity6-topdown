namespace Quantum
{
    using Photon.Deterministic;

    // WeaponWeight (docs/hero-mastery.md's "Initial Weight Behavior") is a general weapon property,
    // not a Hero Mastery concept - Hero Mastery (HeroMasteryUtility/WeaponWeightMasteryData) just
    // queries WeaponDataAsset.Weight the same way it used to query Family, and this move-speed effect
    // lives here instead, alongside it, so the two never duplicate each other's resolution logic.
    public static unsafe class WeaponWeightUtility
    {
        private static readonly FP LightMoveSpeedMultiplier = FP.FromString("1.10");
        private static readonly FP HeavyMoveSpeedMultiplier = FP.FromString("0.90");

        // Read by PlayerMovementProcessor.BeforeMove, the same "targetSpeed *= X.ResolveY(...)" idiom
        // every other move-speed contributor there already uses (CharacterStats/StatusEffect/
        // MutationModifier) - 1x (no effect) for Medium or for an owner with no equipped weapon.
        public static FP GetMoveSpeedMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return FP._1;

            switch (f.FindAsset(weapon->WeaponData).Weight)
            {
                case WeaponWeight.Light: return LightMoveSpeedMultiplier;
                case WeaponWeight.Heavy: return HeavyMoveSpeedMultiplier;
                default: return FP._1;
            }
        }
    }
}
