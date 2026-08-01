namespace Quantum
{
    using Photon.Deterministic;

    // Reaches into Weapon directly, same pattern MagazineSizeUpgradeData already uses - collapses
    // the magazine to 1 (every shot is the last bullet) and bakes FinalRoundDamageBonus, the exact
    // field FinalRoundWeaponPerkData/WeaponSystem.ResolveLiveDamage already read live off
    // Ammo == 1, so every shot gets it for free, no new plumbing. Known limitation shared with
    // MagazineSizeUpgradeData: a later weapon pickup resets Weapon.MagazineSize/
    // FinalRoundDamageBonus from that weapon's own data - nothing re-applies Rift Mutations on
    // equip. See docs/rift-mutations.md.
    public unsafe class OneInTheChamberMutationData : RiftMutationData
    {
        public FP FinalRoundDamageBonus = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return;

            weapon->MagazineSize = 1;
            weapon->Ammo = weapon->Ammo > 1 ? 1 : weapon->Ammo;
            weapon->FinalRoundDamageBonus += FinalRoundDamageBonus;
        }

        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(FinalRoundDamageBonus * 100) };
    }
}
