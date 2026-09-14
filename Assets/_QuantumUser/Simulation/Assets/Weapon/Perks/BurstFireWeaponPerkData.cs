namespace Quantum
{
    using Photon.Deterministic;

    // Double Barrel/Burst Rifle's signature - bakes onto WeaponBurstState the same way every other
    // perk bakes onto its own optional component, idle (ShotsRemaining 0) until WeaponSystem.Update
    // actually starts a burst. Unlike every other perk here, WeaponSystem also reads BurstCount/
    // Delay/CritOnFinalShot back OFF this component at that point (rather than off a flat asset
    // field) to know it's dealing with a bursting weapon at all - see WeaponBurstState's own comment
    // and WeaponSystem.Update's burst-start block. Not offered through the roll pool today (no
    // weapon mid-run should suddenly start bursting) - authored directly into a weapon's own
    // WeaponDataAsset.BaseTraits instead, same as FinalRoundWeaponPerkData/RelentlessFireWeaponPerkData
    // are for Frost Revolver/Drum SMG.
    public unsafe class BurstFireWeaponPerkData : WeaponPerkData
    {
        public int BurstCount = 2;
        public FP BurstDelay;
        public bool CritOnFinalBurstShot;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponBurstState>(owner, out var burst);
            *burst = default;
            burst->BurstCount = BurstCount;
            burst->Delay = BurstDelay;
            burst->CritOnFinalShot = CritOnFinalBurstShot;
        }

        protected override object[] DescriptionArgs => new object[] { BurstCount };
    }
}
