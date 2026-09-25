namespace Quantum
{
    // What a draw site knows about the weapon a perk would land on - see WeaponPerkData.SupportsWeapon.
    // Fire type alone wasn't enough: an AreaHitData launcher (Grenade/Cluster/Napalm) is a Projectile
    // weapon, but nothing that is only read by DirectHitData (pierce, bounce, Quantum Rounds,
    // Explosive Sequence/Cataclysm, element infusion) ever reaches its blast, so those perks were
    // offered as dead picks there.
    public struct WeaponPerkTarget
    {
        public WeaponFireType FireType;

        // A single-target contact the per-hit weapon perks can read: a DirectHitData projectile, or
        // any hitscan beam (FireHitscan reads the same perk state live).
        public bool HasDirectHit;

        public ElementType Element;

        // The permissive case - every perk in the pool can express itself on a plain direct-hit
        // projectile, so an unresolvable weapon can only ever cost a perk that would have been
        // filtered, never one that should have been offered.
        public static WeaponPerkTarget Default => new WeaponPerkTarget
        {
            FireType = WeaponFireType.Projectile,
            HasDirectHit = true,
            Element = ElementType.Neutral,
        };

        public static WeaponPerkTarget Resolve(Frame f, AssetRef<WeaponDataAsset> weaponDataRef)
        {
            if (weaponDataRef.IsValid == false)
                return Default;

            WeaponDataAsset data = f.FindAsset(weaponDataRef);

            if (data == null)
                return Default;

            ProjectileHitData hit = null;

            if (data.FireType == WeaponFireType.Projectile && data.ProjectileData.IsValid == true)
            {
                ProjectileDataAsset projectile = f.FindAsset(data.ProjectileData);

                if (projectile != null && projectile.Hit.IsValid == true)
                {
                    hit = f.FindAsset(projectile.Hit);
                }
            }

            return From(data, hit);
        }

        // Frame-free half, shared with the Editor-side Balance Simulator (which resolves assets
        // through QuantumUnityDB instead). A projectile weapon whose Hit can't be resolved is treated
        // as direct-hit, same permissive fallback as Default.
        public static WeaponPerkTarget From(WeaponDataAsset data, ProjectileHitData hit)
        {
            if (data == null)
                return Default;

            return new WeaponPerkTarget
            {
                FireType = data.FireType,
                HasDirectHit = data.FireType != WeaponFireType.Projectile || hit == null || hit is DirectHitData,
                Element = data.Element,
            };
        }
    }
}
