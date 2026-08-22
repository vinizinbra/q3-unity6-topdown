namespace Quantum
{
    // Valid explosion hits from this weapon apply 1 Rift Mark to each enemy caught, once per
    // explosion event - only real weapon-proc explosions (DirectHitData.ApplyTerminalWeaponPerks/
    // WeaponSystem.ApplyHitscanTerminalPerks' own HitEffectUtility.ApplyExplosion call sites) qualify,
    // never secondary damage ticks or decorative effects. See docs/weapon-perks.md.
    public unsafe class UnstablePayloadWeaponPerkData : WeaponPerkData
    {
        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponHitTrackingPerks>(owner, out var tracking);
            tracking->HasUnstablePayload = true;
        }
    }
}
