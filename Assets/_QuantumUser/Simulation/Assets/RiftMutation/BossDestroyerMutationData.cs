namespace Quantum
{
    using Photon.Deterministic;

    // Trades trash-clear for heavy-hitter damage: bonus damage against Heavy/Elite/Boss enemies, a
    // penalty against Filler/Normal/Specialist ones.
    //
    // Reads the target's own EnemyDataAsset.Tier generically
    // (DamageUtility.ResolveTargetTierDamageMultiplier), so any current or future enemy is covered by
    // its tier alone - no per-enemy-type list anywhere. Applied inside ResolveOutgoingDamage
    // alongside every other All Damage term, so it affects weapon/skill/element-reaction damage alike
    // whenever the hit is attributed to this player.
    public unsafe class BossDestroyerMutationData : RiftMutationData
    {
        public FP HeavyTierDamageBonus = FP._0;
        public FP LightTierDamagePenalty = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->BossDestroyerHeavyDamageBonus = FPMath.Max(stats->BossDestroyerHeavyDamageBonus, HeavyTierDamageBonus);
            stats->BossDestroyerLightDamagePenalty = FPMath.Max(stats->BossDestroyerLightDamagePenalty, LightTierDamagePenalty);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            HeavyTierDamageBonus.AsFloat * 100f,
            LightTierDamagePenalty.AsFloat * 100f
        };
    }
}
