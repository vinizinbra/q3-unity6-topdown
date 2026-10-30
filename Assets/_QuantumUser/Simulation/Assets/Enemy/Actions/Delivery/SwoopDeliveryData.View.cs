namespace Quantum
{
    // View-only half of SwoopDeliveryData: one AttackVisualStep per stage (index = stage - 1: Rise, Hover, Aim, Dive),
    // played by EnemyAttackVisualsView the frame the simulation enters that stage (IStagedDeliveryVisuals).
    public partial class SwoopDeliveryData : IStagedDeliveryVisuals
    {
        public AttackVisualStep[] StageSteps = new AttackVisualStep[0];

        public AttackVisualStep GetStageStep(int stage)
        {
            int index = stage - 1;
            return StageSteps != null && index >= 0 && index < StageSteps.Length ? StageSteps[index] : null;
        }
    }
}
