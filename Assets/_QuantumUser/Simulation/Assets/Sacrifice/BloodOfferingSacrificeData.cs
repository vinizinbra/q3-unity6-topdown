namespace Quantum
{
    using Photon.Deterministic;

    // Permanently reduces the sacrificing player's own Max Health by a percentage - same
    // multiplicative-compose pattern GlassCoreMutationData/LastBastionMutationData already use
    // (CharacterStats.MaxHealthMultiplier *= factor, then CharacterSystem.RefreshMaxHealth), so
    // repeated picks across multiple Breathing Breaks compound correctly rather than needing a
    // separately-tracked absolute baseline. See docs/breathing-poi.md.
    public unsafe class BloodOfferingSacrificeData : SacrificeDefinition
    {
        public FP HealthPercent = FP._0_20;

        // Eligibility floor - a player already low enough that this sacrifice would drop their
        // projected Max Health below this can't offer it (see IsEligible).
        public FP MinimumMaxHealth = 1;

        public override bool IsEligible(Frame f, EntityRef entity)
        {
            return TryProjectMaxHealth(f, entity, out FP projected) && projected >= MinimumMaxHealth;
        }

        public override void ApplyCost(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MaxHealthMultiplier *= (FP._1 - HealthPercent);
            CharacterSystem.RefreshMaxHealth(f, entity);
        }

        public override string BuildValuePreview(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return string.Empty;

            FP after = TryProjectMaxHealth(f, entity, out FP projected) ? projected : FP._0;

            return $"MAX HP\n{health->MaxHealth.AsFloat:0} -> {after.AsFloat:0}";
        }

        private bool TryProjectMaxHealth(Frame f, EntityRef entity, out FP projected)
        {
            projected = FP._0;

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return false;

            CharacterData data = f.FindAsset(stats->CharacterData);

            if (data == null)
                return false;

            projected = data.BaseMaxHealth * stats->MaxHealthMultiplier * (FP._1 - HealthPercent);
            return true;
        }
    }
}
