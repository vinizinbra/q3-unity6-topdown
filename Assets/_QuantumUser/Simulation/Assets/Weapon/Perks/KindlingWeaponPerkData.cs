namespace Quantum
{
    using Photon.Deterministic;

    // Fire SMG's signature - bonus weapon damage against a Burning target. Read live in
    // DamageUtility.ResolveOutgoingDamage off WeaponConditionalDamage, so it only amplifies real
    // weapon hits, never the Burn ticks themselves. Authored into WeaponDataAsset.BaseTraits today
    // (not the roll pool), same as CritStunWeaponPerkData.
    public unsafe class KindlingWeaponPerkData : WeaponPerkData
    {
        public FP DamageBonus = FP._0_25;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponConditionalDamage>(owner, out var conditional);
            conditional->BurningTargetDamageBonus += DamageBonus;
        }

        protected override object[] DescriptionArgs => new object[] { DamageBonus.AsFloat * 100f };
    }
}
