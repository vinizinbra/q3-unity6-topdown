namespace Quantum
{
    using Photon.Deterministic;

    // Instantly heals the buyer for a percent of their own MaxHealth - same percent-of-target
    // convention HealUtility.ApplyHeal/HealingShrine already use.
    public class HealFoodOfferData : FoodOfferData
    {
        public FP HealPercent = FP._0_50;

        public override void Apply(Frame f, EntityRef buyer)
        {
            HealUtility.ApplyHeal(f, buyer, buyer, HealPercent);
        }
    }
}
