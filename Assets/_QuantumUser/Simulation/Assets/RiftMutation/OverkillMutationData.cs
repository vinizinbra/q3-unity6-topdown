namespace Quantum
{
    using Photon.Deterministic;

    // Damage dealt past what an enemy had left is not wasted - a fraction of it detonates at the
    // corpse, so an over-tuned hit spills into the pack instead of evaporating on a target that was
    // already dead.
    //
    // The excess is read straight out of the damage pipeline (DamageUtility already computes the
    // unclamped post-hit health, which IS the overkill), so this needed no restructuring of damage
    // resolution - only a capture before Cheat Death rewrites that value. See OverkillUtility.
    //
    // Recursion is bounded by flagging the blast as a chained explosion, the same brake Pixie's
    // Chain Reaction already terminates on: a chained blast can never produce another Overkill.
    public unsafe class OverkillMutationData : RiftMutationData
    {
        public FP OverkillConversion = FP._0;
        public FP ExplosionRadius = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->OverkillConversion = FPMath.Max(stats->OverkillConversion, OverkillConversion);
            stats->OverkillRadius = FPMath.Max(stats->OverkillRadius, ExplosionRadius);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            OverkillConversion.AsFloat * 100f,
            ExplosionRadius.AsFloat
        };
    }
}
