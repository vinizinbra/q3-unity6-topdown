namespace Quantum
{
    // View-only half of AirborneKneelDeliveryData: one AttackVisualStep per stage (index = stage - 1: Hit, Fall, Kneel,
    // Rise), played by EnemyAttackVisualsView the frame the simulation enters that stage (IStagedDeliveryVisuals).
    public partial class AirborneKneelDeliveryData : IStagedDeliveryVisuals
    {
        public AttackVisualStep[] StageSteps = new AttackVisualStep[0];

        public AttackVisualStep GetStageStep(int stage)
        {
            int index = stage - 1;
            return StageSteps != null && index >= 0 && index < StageSteps.Length ? StageSteps[index] : null;
        }
    }
}
