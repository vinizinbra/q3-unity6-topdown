namespace Quantum
{
    using Photon.Deterministic;

    // Applying Burn/Chill/Shock heals HealFraction of your Max Health, at most once per Cooldown
    // seconds. Only offered while the equipped weapon can actually apply an element.
    public unsafe class ElementalBestowmentMutationData : RiftMutationData
    {
        public FP HealFraction = FP.FromString("0.02");
        public FP Cooldown = FP.FromString("1.5");

        public override bool IsEligible(Frame f, EntityRef entity)
        {
            if (f.Has<WeaponElementInfusion>(entity) == true)
                return true;

            return f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true
                && weapon->WeaponData.IsValid == true
                && f.FindAsset(weapon->WeaponData).Element != ElementType.Neutral;
        }

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->ElementHealFraction = HealFraction;
            stats->ElementHealCooldown = Cooldown;
        }

        protected override object[] DescriptionArgs => new object[] { HealFraction.AsFloat * 100f, Cooldown.AsFloat };
    }
}
