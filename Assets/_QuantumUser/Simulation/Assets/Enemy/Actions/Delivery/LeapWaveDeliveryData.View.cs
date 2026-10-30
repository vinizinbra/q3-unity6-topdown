namespace Quantum
{
    // View-only half of LeapWaveDeliveryData: one AttackVisualStep per stage (index = stage - 1: Air, Wave), played by
    // EnemyAttackVisualsView the frame the simulation enters that stage (IStagedDeliveryVisuals) - so the landing
    // pose/impact lands exactly at touchdown.
    public partial class LeapWaveDeliveryData : IStagedDeliveryVisuals
    {
        public AttackVisualStep[] StageSteps = new AttackVisualStep[0];

        public AttackVisualStep GetStageStep(int stage)
        {
            int index = stage - 1;
            return StageSteps != null && index >= 0 && index < StageSteps.Length ? StageSteps[index] : null;
        }
    }
}
