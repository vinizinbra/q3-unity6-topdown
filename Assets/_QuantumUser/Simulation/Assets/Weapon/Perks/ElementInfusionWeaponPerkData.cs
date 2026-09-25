namespace Quantum
{
    using Photon.Deterministic;

    // Grafts an EXTRA on-hit element onto the weapon (see WeaponElementInfusion) - independent of the
    // weapon's own WeaponDataAsset.Element, which keeps applying unconditionally as before (see
    // StatusEffectUtility.TryApplyElementalStatus). This one rolls its own ProcChance, so a Neutral
    // weapon gains an element and an already-elemental weapon lands two side by side (base +
    // infused). Only one infused element per weapon:
    // a second Element Infusion perk last-wins, overwriting both fields (see docs/weapon-perks.md).
    public unsafe class ElementInfusionWeaponPerkData : WeaponPerkData
    {
        public ElementType Element = ElementType.Fire;
        public FP ProcChance = FP._0_25;

        // Direct contacts only (AreaHitData.Detonate never carries PerkElement), and never onto a
        // weapon whose own element already IS this one - the native element already applies on
        // every hit, so a 100% infusion of the same element just ran the baseline twice per hit
        // (double Burn stacks, and a Lightning weapon's first hit reading as "already shocked").
        public override bool SupportsWeapon(in WeaponPerkTarget target) => target.HasDirectHit && target.Element != Element;

        // Only one infused element per weapon - a second one overwrites the first (see Apply).
        public override bool ConflictsWith(WeaponPerkData owned) => owned is ElementInfusionWeaponPerkData;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            // Backstop for any path that skips the draw filter (base traits, cheats) - see
            // SupportsWeapon for why a same-element infusion must never apply.
            if (weapon->WeaponData.IsValid == true && f.FindAsset(weapon->WeaponData).Element == Element)
                return;

            f.AddOrGet<WeaponElementInfusion>(owner, out var infusion);
            infusion->Element = Element;
            infusion->ProcChance = ProcChance;
        }

        protected override object[] DescriptionArgs => new object[] { Element, ProcChance.AsFloat * 100f };
    }
}
