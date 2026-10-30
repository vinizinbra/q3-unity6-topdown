namespace Quantum
{
    using Photon.Deterministic;

    // Opt-in presentation hook for deliveries with sub-stages the four AttackVisualStep phase edges can't express
    // (a swoop's rise/hover/aim/dive, an airborne stagger's hit/fall/kneel/rise). The delivery writes the current
    // stage into the EnemyDeliveryStage component (DeliveryStageUtility.Set); EnemyAttackVisualsView polls it and
    // plays GetStageStep(stage) - so every pose change happens on the simulation beat it belongs to.
    public interface IStagedDeliveryVisuals
    {
        AttackVisualStep GetStageStep(int stage);
    }

    public static unsafe class DeliveryStageUtility
    {
        public static void Set(Frame f, EntityRef entity, byte stage)
        {
            f.AddOrGet<EnemyDeliveryStage>(entity, out var stageState);
            stageState->Stage = stage;
            stageState->Serial++;
        }

        public static void Clear(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<EnemyDeliveryStage>(entity, out var stageState) == true)
            {
                stageState->Stage = 0;
                stageState->Serial++;
            }
        }
    }
}
