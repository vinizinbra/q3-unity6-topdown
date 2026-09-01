namespace Quantum
{
    using Photon.Deterministic;

    // Limited-hit survival: far more Accessory durability, far less health behind it - so the
    // Accessory stops being a small buffer in front of a health pool and becomes most of what is
    // actually keeping you alive, and recovering it after every block becomes the whole game.
    //
    // Both halves are MULTIPLIERS, and deliberately so. An earlier version set Max Health to an
    // absolute 1, which read well on paper but had two problems: it silently overwrote every Max
    // Health pick a player had already made (an absolute target has to, or it isn't absolute), and a
    // percentage-of-max-health cost anywhere else in the game floored to nothing against it - which
    // is exactly why it needed a hand-authored mutual exclusion with Infinite Momentum. Halving
    // instead composes with the rest of the build normally and needs no special-case rule.
    //
    // Because AccessoryGuardUtility.Restore sets current durability from max, the raised maximum
    // keeps applying across every later recovery, repair and replacement with nothing to maintain here.
    public unsafe class GlassCoreMutationData : RiftMutationData
    {
        public FP DurabilityMultiplier = FP._1;
        public FP HealthMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            AccessoryGuardUtility.ScaleMaxDurability(f, entity, DurabilityMultiplier);

            stats->MaxHealthMultiplier = FPMath.Max(FP._0, stats->MaxHealthMultiplier * HealthMultiplier);
            CharacterSystem.RefreshMaxHealth(f, entity);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            DurabilityMultiplier.AsFloat,
            (HealthMultiplier.AsFloat - 1f) * 100f
        };
    }
}
