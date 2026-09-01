namespace Quantum
{
    using Photon.Deterministic;

    // Single-shot weapon gameplay: one round in the magazine, and that round hits enormously hard.
    //
    // Both halves are PERSISTENT owner-level modifiers rather than edits to the equipped weapon
    // instance - which is the whole point. The magazine size is an absolute override applied by
    // WeaponSystem.ApplyOwnerWeaponModifiers as a stage of every Equip (and deliberately beating
    // any magazine BONUS, so this still means exactly one round for a player who also took Bullet
    // Storm); the damage rides CharacterStats.WeaponDamageMultiplier, which is resolved live per
    // shot. Neither is touched by picking up a new weapon.
    //
    // This replaces an earlier implementation that wrote Weapon.MagazineSize plus a
    // WeaponMagazinePositionPerks "final round" bonus directly. Both are wiped by
    // WeaponSystem.SeedStats/SeedPerkRoster on the next equip, so that version silently stopped
    // working the first time the player picked up a weapon.
    public unsafe class OneInTheChamberMutationData : RiftMutationData
    {
        public int MagazineSize = 1;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->MagazineSizeOverride = MagazineSize < 1 ? 1 : MagazineSize;
            stats->WeaponDamageMultiplier = FPMath.Max(FP._0, stats->WeaponDamageMultiplier * DamageMultiplier);

            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true)
            {
                WeaponSystem.ApplyOwnerWeaponModifiers(f, entity, weapon);
            }
        }

        protected override object[] DescriptionArgs => new object[]
        {
            MagazineSize,
            (DamageMultiplier.AsFloat - 1f) * 100f
        };
    }
}
