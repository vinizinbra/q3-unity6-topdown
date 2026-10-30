namespace Quantum
{
    // View-only half of IceChargeDeliveryData: one AttackVisualStep per stage (index = stage - 1: Run, WallStun), played
    // by EnemyAttackVisualsView the frame the simulation enters that stage (IStagedDeliveryVisuals).
    public partial class IceChargeDeliveryData : IStagedDeliveryVisuals
    {
        public AttackVisualStep[] StageSteps = new AttackVisualStep[0];

        public AttackVisualStep GetStageStep(int stage)
        {
            int index = stage - 1;
            return StageSteps != null && index >= 0 && index < StageSteps.Length ? StageSteps[index] : null;
        }
    }
}
