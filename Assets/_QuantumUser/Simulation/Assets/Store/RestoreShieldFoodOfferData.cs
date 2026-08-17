namespace Quantum
{
    using Photon.Deterministic;

    // Instantly restores a percent of the buyer's own Max Shield - same percent-of-target
    // convention ShieldUtility.ApplyShield already uses.
    public class RestoreShieldFoodOfferData : FoodOfferData
    {
        public FP ShieldPercent = FP._0_50;

        public override void Apply(Frame f, EntityRef buyer)
        {
            ShieldUtility.ApplyShield(f, buyer, buyer, ShieldPercent);
        }
    }
}
