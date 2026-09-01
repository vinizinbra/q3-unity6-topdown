namespace Quantum
{
    using Photon.Deterministic;

    // Aggressive close-range movement: hit much harder up close, much softer at range, and get a
    // burst of speed for every close kill so repositioning between them is part of the loop.
    //
    // The damage half is the shared attacker-side range falloff
    // (DamageUtility.ResolveRangeDamageMultiplier, lerped between the two multipliers). The kill
    // reaction is in RiftMutationReactionSystem.OnEntityKilled, gated on the SAME near threshold the
    // damage bonus uses - one definition of "close", not two.
    //
    // Longshot is the opposing pick but deliberately NOT authored as incompatible: the two genuinely
    // compose into a flat, uninteresting build rather than a degenerate one, so partially cancelling
    // is a fair outcome of a player's own choice.
    public unsafe class CloseQuartersMutationData : RiftMutationData
    {
        public FP NearMultiplier = FP._1;
        public FP FarMultiplier = FP._1;
        public FP KillMoveSpeedBonus = FP._0;
        public FP KillMoveSpeedDuration = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->NearDamageMultiplier = FPMath.Max(FP._0, stats->NearDamageMultiplier * NearMultiplier);
            stats->FarDamageMultiplier = FPMath.Max(FP._0, stats->FarDamageMultiplier * FarMultiplier);

            stats->NearKillMoveSpeedBonus = FPMath.Max(stats->NearKillMoveSpeedBonus, KillMoveSpeedBonus);
            stats->NearKillMoveSpeedDuration = FPMath.Max(stats->NearKillMoveSpeedDuration, KillMoveSpeedDuration);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (NearMultiplier.AsFloat - 1f) * 100f,
            (FarMultiplier.AsFloat - 1f) * 100f,
            KillMoveSpeedBonus.AsFloat * 100f,
            KillMoveSpeedDuration.AsFloat
        };
    }
}
