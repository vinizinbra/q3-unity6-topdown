namespace Quantum
{
    using Photon.Deterministic;

    // Rewards hoovering up a burst of drops: collect enough collectibles fast enough and you get a
    // short window of speed and fire rate.
    //
    // "Valid collectible" is defined by which signal this listens to, not by a list of item types -
    // OnCollectibleCollected fires only from the currency-orb pickup path, so Accessory recoveries,
    // Merchant purchases and static interactables are excluded structurally and can never be
    // accidentally included by a future pickup type landing in the wrong category.
    //
    // The payoff rides the generic timed-buff slots, so it follows the project's normal refresh
    // behaviour rather than inventing a stacking rule of its own.
    public unsafe class ScavengerRushMutationData : RiftMutationData
    {
        public byte RequiredPickups = 5;
        public FP CollectionWindow = FP._0;
        public FP BuffDuration = FP._0;
        public FP MoveSpeedBonus = FP._0;
        public FP FireRateBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->ScavengerRequiredPickups = RequiredPickups < 1 ? (byte)1 : RequiredPickups;
            stats->ScavengerWindow = FPMath.Max(stats->ScavengerWindow, CollectionWindow);
            stats->ScavengerBuffDuration = FPMath.Max(stats->ScavengerBuffDuration, BuffDuration);
            stats->ScavengerMoveSpeedBonus = FPMath.Max(stats->ScavengerMoveSpeedBonus, MoveSpeedBonus);
            stats->ScavengerFireRateBonus = FPMath.Max(stats->ScavengerFireRateBonus, FireRateBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            RequiredPickups,
            CollectionWindow.AsFloat,
            MoveSpeedBonus.AsFloat * 100f,
            FireRateBonus.AsFloat * 100f,
            BuffDuration.AsFloat
        };
    }
}
