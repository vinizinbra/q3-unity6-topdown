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

        [Tooltip("Scales MovementDataAsset's WalkSpeed/RunSpeed, which are shared across all characters.")]
        public FP MoveSpeedMultiplier = FP._1;

        [Tooltip("Only reaches the entity if its prototype carries the matching component.")]
        public FP BaseArmor = FP._0;

        [Header("Shield")]
        public FP BaseMaxShield = FP._0;
        public FP ShieldRechargeDelay = 3;
        public FP ShieldRechargeRate = 20;

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

        public FP CooldownMultiplier = FP._1;

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
        public FP KnockbackTakenMultiplier = FP._1;
        public FP HealingReceivedMultiplier = FP._1;

        [Header("Utility")]
        public FP PickupRangeMultiplier = FP._1;
        public FP Luck = FP._0;

        [Header("View")]
        [Tooltip("Tint for the local player's ground ring/glow/movement-arrow (see MovementRingView) - lets each hero's \"this is you\" marker read as their own color.")]
        public Color RingColor = Color.white;

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
        [Tooltip("What this hero may be offered mid-run for each slot, granted via SkillSystem.AddUpgrade. Every hero can share one DashSkillData - these lists are what make their dashes diverge, rather than a per-hero skill asset per upgrade combination.")]
        [FormerlySerializedAs("PrimarySkillUpgrades")]
        [ExpandableAsset] public List<AssetRef<SkillActionData>> DashSkillUpgrades = new();
        [FormerlySerializedAs("MobilitySkillUpgrades")]
        [ExpandableAsset] public List<AssetRef<SkillActionData>> HeroSkillUpgrades = new();

        [Tooltip("What this hero may be offered mid-run as a Passive Upgrade level-up choice - see LevelUpPoolKind.PassiveUpgrade. Per-hero same as DashSkillUpgrades/HeroSkillUpgrades above, since a hero's passive upgrades build on its own single Passive. No grant mechanism exists yet (see PassiveUpgradeUtility) - plumbing only.")]
        [ExpandableAsset] public List<AssetRef<PassiveUpgradeData>> PassiveUpgrades = new();
    }
}
