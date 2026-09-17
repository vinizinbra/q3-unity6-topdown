namespace Quantum
{
    using System.Collections.Generic;
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

        // A Weapon-sourced hit with a non-Neutral Element applies its matching baseline status
        // unconditionally (Fire->Burn, Ice->Chill, Lightning->Shock) and, if the target already
        // carries the OTHER status of a reaction pair, fires that pairwise reaction immediately -
        // see StatusEffectUtility.TryApplyElementalStatus and docs/elemental-reactions.md. Carried
        // through Projectile/AreaOwner so a weapon's projectile hits and its spawned areas (e.g. a
        // grenade's blast) both proc it.
        public ElementType Element = ElementType.Neutral;

        // Which of the 6 player weapon families this is (Pistol/SMG/AssaultRifle/Shotgun/Sniper/
        // GrenadeLauncher) - identity/perk-pool/UI/balance classification, independent of Element and
        // Weight below and never coupled to either (see docs/hero-mastery.md). None (default) for
        // anything that isn't one of the 6 (Lux's sentry guns, test/basic weapons).
        public WeaponFamily Family = WeaponFamily.None;

        // Which weight class this weapon belongs to - the axis Hero Mastery's Weapon Weight track keys
        // off (see docs/hero-mastery.md) and that WeaponWeightUtility.GetMoveSpeedMultiplier reads for
        // the generic Light/Medium/Heavy move-speed modifier (docs/hero-mastery.md's "Initial Weight
        // Behavior"). Independent of Family - explicit per weapon asset, never inferred from Family at
        // runtime, so a future weapon can freely be any (Family, Weight) combination. Medium (default)
        // is the neutral tier for anything not yet explicitly tagged.
        public WeaponWeight Weight = WeaponWeight.Medium;

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

        // This weapon's own baseline WeaponPerkData picks - baked at Equip
        // (WeaponSystem.ApplyBaseTraits) exactly like a rolled Weapon.Perks entry (same
        // Apply(f, owner, weapon) dispatch, ApplyPerks reused verbatim), just from this list instead
        // of a random roll. Lets a weapon's own signature (Double Barrel/Burst Rifle's
        // BurstFireWeaponPerkData; Frost Revolver's FinalRoundWeaponPerkData; Drum SMG's
        // SuppressiveCycleWeaponPerkData; Cluster Launcher's SplitShotWeaponPerkData; Hellshot's
        // ExplosiveCritWeaponPerkData; Disruptor's CritStunWeaponPerkData) reuse the exact same
        // perk classes/components/reaction systems a real roll would, instead of the weapon asset
        // growing a new flat field per signature. Doesn't count against Weapon.Perks' 5-slot roll
        // cap - a fully-rolled weapon keeps its own signature on top of 5 more picks. Empty (default)
        // for a weapon with no baseline signature at all (Arcshot/Slugger express theirs entirely
        // through BonusBounces/PelletCount above - pure data, no perk needed).
        [ExpandableAsset] public List<AssetRef<WeaponPerkData>> BaseTraits = new();
    }
}
