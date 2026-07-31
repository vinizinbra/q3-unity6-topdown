namespace Quantum
{
    using Photon.Deterministic;

    public enum WeaponFireType
    {
        Hitscan,
        Projectile
    }

    public partial class WeaponDataAsset : AssetObject
    {
        public WeaponFireType FireType = WeaponFireType.Projectile;

        public FP Damage = 10;

        // Deterministic, not chance-based - every Weapon-sourced hit with a non-Neutral Element
        // applies its matching status (Fire->Burn, Ice->Slow, Poison->Poison, Lightning->Stun), see
        // StatusEffectUtility.TryApplyElementalStatus. Carried through Projectile/AreaOwner so a
        // weapon's projectile hits and its spawned areas (e.g. a grenade's blast) both proc it.
        public ElementType Element = ElementType.Neutral;

        public FP CriticalChance;
        public FP CriticalDamageBonus;

        public FP FireCooldownTime = FP._0_25;

        public int MagazineSize = 12;
        public FP ReloadDuration = 1;

        public FP Range = 50;

        // Pellets fired per trigger pull, evenly cone-spread across SpreadAngle around the aim
        // direction - see WeaponSystem.GetPelletAngle. 1 (default) is a no-op: every existing
        // single-shot weapon behaves identically. Damage above is PER PELLET, not the volley
        // total - same convention FanProjectileDeliveryData uses for its enemy-only equivalent.
        public int PelletCount = 1;

        // Full cone width in degrees, meaningless while PelletCount <= 1.
        public FP SpreadAngle;

        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;
    }
}
