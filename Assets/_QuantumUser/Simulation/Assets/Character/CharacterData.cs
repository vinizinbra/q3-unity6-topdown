namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine;
    using UnityEngine.Serialization;

    // One asset per hero. Seeds the CharacterStats component on add (see CharacterSystem); upgrades
    // then mutate the component, never this.
    public unsafe partial class CharacterData : AssetObject
    {
        [Header("Core")]
        public FP BaseMaxHealth = 100;

        [Tooltip("Flat HP/sec regenerated while below max. 0 for most heroes - raised by HealthRegenUpgradeData level-up grants, not meant as a large baseline.")]
        public FP BaseHealthRegenRate = FP._0;

        [Tooltip("Scales MovementDataAsset's WalkSpeed/RunSpeed, which are shared across all characters.")]
        public FP MoveSpeedMultiplier = FP._1;

        [Tooltip("Only reaches the entity if its prototype carries the matching component.")]
        public FP BaseArmor = FP._0;

        [Header("Shield")]
        public FP BaseMaxShield = FP._0;

        [Tooltip("Shield is filled by gameplay only, never by time - it starts a run EMPTY and never recharges. On for every hero: Shield is an earned, spendable buffer whose job is to keep your Accessory on your head (while you have any Shield, hits eat Shield and the accessory never pops off). Leave OFF for anything that should recharge the classic way. Suppresses the RechargeDelay/RechargeRate fields below entirely.")]
        public bool ShieldChargeOnly = true;

        [Tooltip("Ignored while Shield Charge Only is on.")]
        public FP ShieldRechargeDelay = 3;

        [Tooltip("Ignored while Shield Charge Only is on.")]
        public FP ShieldRechargeRate = 20;

        [Tooltip("How long a charge-only Shield holds its Current value after the most recent successful grant before snapping to 0 - a single shared timer, refreshed (not extended/stacked) by every gain. 0 disables expiration entirely (a plain persistent charge-only shield). Brute is the only hero authoring this above 0 - see docs/brute-ascensions.md's Temporary Shield section.")]
        public FP ShieldTemporaryDuration = FP._0;

        [Header("Offense")]
        [Tooltip("Scales every hit. WeaponDamageMultiplier/SkillDamageMultiplier stack on top of it and apply only to their own DamageSource.")]
        public FP DamageMultiplier = FP._1;
        public FP WeaponDamageMultiplier = FP._1;
        public FP SkillDamageMultiplier = FP._1;

        public FP CriticalChance = FP._0_05;
        public FP CriticalDamageMultiplier = FP._1_50;

        public FP ElementalChance = FP._0;

        public FP AttackSpeedMultiplier = FP._1;
        public FP ReloadSpeedMultiplier = FP._1;

        public FP ProjectileSpeedMultiplier = FP._1;
        public FP AreaRadiusMultiplier = FP._1;

        [Tooltip("Attacker-side range falloff (Close Quarters/Longshot Global Upgrades) - lerped between off attacker-target distance, see DamageUtility.ResolveRangeDamageMultiplier.")]
        public FP NearDamageMultiplier = FP._1;
        public FP FarDamageMultiplier = FP._1;

        public FP DashCooldownMultiplier = FP._1;
        public FP SkillCooldownMultiplier = FP._1;

        [Tooltip("Scales how long a skill's spawned effects last - a decoy, a fire trail.")]
        public FP SkillDurationMultiplier = FP._1;

        public FP KnockbackMultiplier = FP._1;

        public FP LifeSteal = FP._0;
        public FP OutgoingStatusDurationMultiplier = FP._1;

        [Header("Defense")]
        [Tooltip("Scales the Base values above - a \"half health, double shield\" perk is MaxHealthMultiplier 0.5 / MaxShieldMultiplier 2, leaving the hero's authored baseline alone.")]
        public FP MaxHealthMultiplier = FP._1;
        public FP MaxShieldMultiplier = FP._1;

        public FP DamageReduction = FP._0;

        [Tooltip("Multiplicative damage-taken scaling (1 = unchanged, 0.9 = takes 10% less). Separate from the additive DamageReduction fraction above - see CharacterStats.qtn.")]
        public FP DamageTakenMultiplier = FP._1;

        public FP KnockbackTakenMultiplier = FP._1;
        public FP HealingReceivedMultiplier = FP._1;

        [Header("Utility")]
        public FP PickupRangeMultiplier = FP._1;
        public FP Luck = FP._0;
        public FP ExperienceGainMultiplier = FP._1;
        public FP RiftShardGainMultiplier = FP._1;
        public FP CoinGainMultiplier = FP._1;

        [Header("View")]
        [Tooltip("Tint for the local player's ground ring/glow/movement-arrow (see MovementRingView) - lets each hero's \"this is you\" marker read as their own color.")]
        public Color RingColor = Color.white;

        [Tooltip("Sprite used to represent this hero on the minimap's player marker (see MinimapWidget). Left unassigned, the marker keeps its prefab's default sprite.")]
        public Sprite PawnSprite;

        [Header("Positioning")]
        [Tooltip("Where this character holds its weapon, added on top of the weapon's own SpawnOffset (see WeaponDataAsset) - e.g. a taller hero's hand sits higher than the authored weapon offset alone accounts for. X is authored as a positive \"facing right\" magnitude and mirrored to negative while facing left (both in the view socket and in sim - see WeaponViewController and StatUtility.GetWeaponHoldOffset), rather than rotating continuously with aim angle the way SpawnOffset does.")]
        [FormerlySerializedAs("WeaponHoldOffset")]
        public FPVector3 WeaponPosition;

        [Header("Gameplay")]
        public AssetRef<EntityPrototype> Prototype;
        [FormerlySerializedAs("PrimarySkill")]
        [ExpandableAsset] public AssetRef<SkillData> DashSkill;
        [FormerlySerializedAs("MobilitySkill")]
        [ExpandableAsset] public AssetRef<SkillData> HeroSkill;
        [ExpandableAsset] public AssetRef<PassiveData> Passive;
        [Tooltip("Equipped via the normal WeaponSystem.Equip path once this hero's prototype is materialized (see CharacterSystem.SeedWeapon) - overwrites whatever Weapon.WeaponData the prototype itself carries, same as DashSkill/HeroSkill above.")]
        [ExpandableAsset] public AssetRef<WeaponDataAsset> StartingWeapon;

        [Header("Upgrades")]
        [Tooltip("What this hero may be offered mid-run for the Dash slot, granted via SkillSystem.AddUpgrade. Every hero can share one DashSkillData - this list is what makes their dashes diverge, rather than a per-hero skill asset per upgrade combination. HeroSkill has no equivalent list - see LevelUpUtility.AddHeroSkillUpgradeCandidates, which pulls straight from HeroSkill's own Actions instead (any entry authored there with Activated == false is a level-up candidate).")]
        [FormerlySerializedAs("PrimarySkillUpgrades")]
        [ExpandableAsset] public List<AssetRef<SkillActionData>> DashSkillUpgrades = new();

        [Tooltip("What this hero may be offered mid-run as a Passive Upgrade level-up choice - see LevelUpPoolKind.PassiveUpgrade. Per-hero same as DashSkillUpgrades/HeroSkillUpgrades above, since a hero's passive upgrades build on its own single Passive. No grant mechanism exists yet (see PassiveUpgradeUtility) - plumbing only.")]
        [ExpandableAsset] public List<AssetRef<PassiveUpgradeData>> PassiveUpgrades = new();
    }
}
