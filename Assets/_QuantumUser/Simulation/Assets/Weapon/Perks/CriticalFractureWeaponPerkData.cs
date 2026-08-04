namespace Quantum
{
    // Critical hits from this weapon apply 1 Rift Mark - per-target cooldown lives on the TARGET
    // (StatusEffects.MarkApplicationCooldowns, RiftMarkCooldownKey.CriticalFracture), shared with the
    // Critical Fracture Rift Mutation so the two can never both stack from the same crit - see
    // WeaponPerkReactionSystem.OnCriticalHit and docs/weapon-perks.md.
    public unsafe class CriticalFractureWeaponPerkData : WeaponPerkData
    {
        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnCritReactions>(owner, out var reactions);
            reactions->HasCriticalFracturePerk = true;
        }
    }
}
