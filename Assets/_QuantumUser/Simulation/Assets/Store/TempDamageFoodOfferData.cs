namespace Quantum
{
    using Photon.Deterministic;

    // Grants a temporary weapon-damage buff - reuses StatusEffectUtility.ApplyTemporaryWeaponDamage/
    // StatusEffects.TemporaryWeaponDamageRemaining/Amount UNCHANGED (already exists, backing Max's
    // Last Stand rank 2/Run & Gun rank 2 - see docs/max-ascensions.md), not a new mechanism.
    public class TempDamageFoodOfferData : FoodOfferData
    {
        public FP Duration = 20;
        public FP DamageBonus = FP._0_50;

        public override void Apply(Frame f, EntityRef buyer)
        {
            StatusEffectUtility.ApplyTemporaryWeaponDamage(f, buyer, Duration, DamageBonus);
        }
    }
}
