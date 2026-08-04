namespace Quantum
{
    // Killing an enemy with this weapon applies 1 Rift Mark to the nearest other valid enemy - see
    // WeaponPerkReactionSystem.OnEntityKilled/WeaponPerkUtility.TryFindNearestEnemy and
    // docs/weapon-perks.md. One kill can only ever transfer one mark, already exactly-once by
    // construction (OnEntityKilled fires once per kill) - no cooldown needed.
    public unsafe class RiftAftershockWeaponPerkData : WeaponPerkData
    {
        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponOnKillReactions>(owner, out var reactions);
            reactions->HasRiftAftershock = true;
        }
    }
}
