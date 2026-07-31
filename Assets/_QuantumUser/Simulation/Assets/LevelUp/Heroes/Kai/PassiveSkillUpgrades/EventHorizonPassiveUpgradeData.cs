namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the Void Field's radius. Additive on top of
    // VoidFieldPassiveData's own authored Radius, same "bonus stacks on authored value" shape
    // SpawnRadiusUpgrade/IncreaseDurationUpgrade already use for skill-side ascensions.
    public unsafe partial class EventHorizonPassiveUpgradeData : PassiveUpgradeData
    {
        public FP RadiusBonus = 2;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ProjectileSlowField>(entity, out var field) == false)
                return;

            field->Radius += RadiusBonus;
        }
    }
}
