namespace Quantum
{
    using Photon.Deterministic;

    // Grants a temporary move-speed buff - see StatusEffectUtility.ApplyTempMoveSpeed (new for this
    // feature, folded into PlayerMovementProcessor alongside CharacterStats.MoveSpeedMultiplier).
    public class TempMoveSpeedFoodOfferData : FoodOfferData
    {
        public FP Duration = 20;
        public FP SpeedMultiplier = FP._1_50;

        public override void Apply(Frame f, EntityRef buyer)
        {
            StatusEffectUtility.ApplyTempMoveSpeed(f, buyer, Duration, SpeedMultiplier);
        }
    }
}
