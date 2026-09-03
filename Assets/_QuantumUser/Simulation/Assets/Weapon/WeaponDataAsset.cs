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
        // Display-only, for a Choose-Weapon level-up/Chest card (see WeaponCardWidget) - not made
        // to derive UpgradeData like WeaponPerkData/SkillActionData/etc, since a rolled weapon has
        // no single Rarity of its own (that lives on each individually-rolled perk instead). No
        // separate Icon field here - see GetIcon() in WeaponDataAsset.View.cs, which reuses the
        // sprite already authored on ViewPrefab's own SpriteRenderer instead of a second
        // hand-authored sprite per weapon.
        public string DisplayName;

        public WeaponFireType FireType = WeaponFireType.Projectile;

        public FP Damage = 10;

        // ElementalChance-gated (CharacterStats.ElementalChance, same roll crit uses) - a Weapon-
        // sourced hit with a non-Neutral Element that rolls a hit applies its matching baseline
        // status (Fire->Burn, Ice->Slow, Rock->Intimidate; Electric/Void have none of their own -
        // their identity lives in hand-authored weapon traits like Pierce/Ricochet instead) and, if
        // the target already carries a Rift Mark, consumes a stack to trigger that element's own
        // reaction - see StatusEffectUtility.TryApplyElementalStatus and docs/elemental-reactions.md.
        // Carried through Projectile/AreaOwner so a weapon's projectile hits and its spawned areas
        // (e.g. a grenade's blast) both proc it.
        public ElementType Element = ElementType.Neutral;

        public FP CriticalChance;
        public FP CriticalDamageBonus;

        // Shots per second (higher = faster) - same "rate" convention as every multiplier that
        // scales it (AttackSpeedMultiplier, FireRateWeaponPerkData, Haste, etc - see
        // StatUtility.GetFireCooldown). WeaponSystem.ResolveLiveFireCooldown converts this to an
        // actual time-between-shots (1 / FireRate) before applying those multipliers/bonuses.
        public FP FireRate = 4;

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

        // Ricochet bounces this weapon starts with, before any Ricochet perk's own BonusBounces
        // stacks on top (WeaponSystem.ApplyProjectilePerks adds both onto Projectile.RemainingBounces) -
        // lets a weapon be inherently bouncy (e.g. a boomerang/chain-lightning-flavored gun) with no
        // perk required. 0 (default) is a no-op, same as every other weapon reproducing prior behavior.
        public int BonusBounces = 0;

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;
    }
}
