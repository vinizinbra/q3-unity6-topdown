namespace Quantum
{
    using Photon.Deterministic;

    // Grafts an EXTRA on-hit element onto the weapon (see WeaponElementInfusion) - independent of the
    // weapon's own WeaponDataAsset.Element, which keeps applying via CharacterStats.ElementalChance as
    // before. This one rolls its own ProcChance, so a Neutral weapon gains an element and an already-
    // elemental weapon lands two side by side (base + infused). Only one infused element per weapon:
    // a second Element Infusion perk last-wins, overwriting both fields (see docs/weapon-perks.md).
    public unsafe class ElementInfusionWeaponPerkData : WeaponPerkData
    {
        public ElementType Element = ElementType.Fire;
        public FP ProcChance = FP._0_25;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponElementInfusion>(owner, out var infusion);
            infusion->Element = Element;
            infusion->ProcChance = ProcChance;
        }

        protected override object[] DescriptionArgs => new object[] { Element, ProcChance.AsFloat * 100f };
    }
}
