namespace Quantum
{
    using Photon.Deterministic;

    // Hits from enemies beyond DamageUtility.RangeDamageNearThreshold are heavily reduced, hits from
    // up close hurt more - see MutationCombatUtility.ResolveIncomingDamage. Keyed on attacker distance,
    // so a mortar shell, a sniper bolt and a far charger's projectile all count as ranged.
    public unsafe class DeflectionShieldMutationData : RiftMutationData
    {
        public FP RangedDamageTaken = FP.FromString("0.40");
        public FP CloseDamageTaken = FP.FromString("1.40");

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->RangedDamageTakenMultiplier = Compose(stats->RangedDamageTakenMultiplier, RangedDamageTaken);
            stats->CloseDamageTakenMultiplier = Compose(stats->CloseDamageTakenMultiplier, CloseDamageTaken);
        }

        // 0 means "off" (read as 1), so it composes multiplicatively from a neutral start.
        private static FP Compose(FP current, FP factor) => (current <= FP._0 ? FP._1 : current) * factor;

        protected override object[] DescriptionArgs => new object[] { (RangedDamageTaken.AsFloat - 1f) * 100f, (CloseDamageTaken.AsFloat - 1f) * 100f };
    }
}
