namespace Quantum
{
    using Photon.Deterministic;

    // Paid for fighting hurt: while your health is below a threshold, you hit harder and move faster.
    //
    // A genuine CONDITION, not a timed buff - which is why it bakes thresholds onto CharacterStats
    // and is evaluated fresh at every read (MutationModifierUtility.IsInDanger) rather than applying
    // something with a duration. Healing back over the line removes both halves on the very next
    // evaluation, with nothing to expire and no state to clean up.
    //
    // Damage is ALL damage, not Weapon damage, so a skill build benefits identically.
    public unsafe class DangerPayMutationData : RiftMutationData
    {
        public FP HealthThreshold = FP._0;
        public FP DamageBonus = FP._0;
        public FP MoveSpeedBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DangerPayHealthThreshold = FPMath.Max(stats->DangerPayHealthThreshold, HealthThreshold);
            stats->DangerPayDamageBonus = FPMath.Max(stats->DangerPayDamageBonus, DamageBonus);
            stats->DangerPayMoveSpeedBonus = FPMath.Max(stats->DangerPayMoveSpeedBonus, MoveSpeedBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            HealthThreshold.AsFloat * 100f,
            DamageBonus.AsFloat * 100f,
            MoveSpeedBonus.AsFloat * 100f
        };
    }
}
