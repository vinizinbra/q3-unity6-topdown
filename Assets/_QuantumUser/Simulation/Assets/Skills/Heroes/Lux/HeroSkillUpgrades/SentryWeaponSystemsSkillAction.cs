namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Sentry Ascension (Weapon Systems, line 1/4) - turns the baseline Cannon-only machine into
    // a full weapons platform, one system per rank: Minigun, then Rocket Pod, then Laser.
    //
    // Every one of those is an ordinary WeaponDataAsset armed into its own barrel slot, NOT bespoke
    // turret code - "periodic burst of 4 shots", "periodic AoE rocket" and "piercing laser" are all
    // just authored weapon data (fire rate, burst, projectile, pierce) running through the same
    // WeaponSystem every other gun in the game uses. Splitting barrels into their own Weapon-carrying
    // entities (see SentryBarrel) is what lets all four fire simultaneously on independent cooldowns
    // against independently-chosen targets, which is exactly the "operate independently according to
    // configured cooldowns" the brief asks for - with no scheduler of its own.
    //
    // Slot 0 is the baseline Cannon and is never touched here (see SpawnSentrySkillAction) - this line
    // owns slots 1..3, so rank 3's fantasy is genuinely Cannon + Minigun + Rockets + Laser at once.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade: re-granting fresh (idempotent) every activation and never removing it
    // means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryWeaponSystemsSkillAction : SkillActionData
    {
        [Tooltip("Slot 1 - unlocked at rank 1.")]
        [ExpandableAsset] public AssetRef<WeaponDataAsset> MinigunWeapon;
        public FPVector3 MinigunOffset = new FPVector3(FP._0_50, FP._0_50, 0);

        [Tooltip("Slot 2 - unlocked at rank 2.")]
        [ExpandableAsset] public AssetRef<WeaponDataAsset> RocketWeapon;
        public FPVector3 RocketOffset = new FPVector3(-FP._0_50, FP._0_50, 0);

        [Tooltip("Slot 3 - unlocked at rank 3.")]
        [ExpandableAsset] public AssetRef<WeaponDataAsset> LaserWeapon;
        public FPVector3 LaserOffset = new FPVector3(0, FP._1, 0);

        public SentryWeaponSystemsSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));

            f.AddOrGet<SentryWeaponUpgrade>(filter.Entity, out var upgrade);

            // SET the full loadout every activation rather than accumulating - a lower rank must never
            // leave a higher rank's slot armed from a previous grant, and re-granting has to be
            // idempotent.
            Arm(upgrade, 1, rank >= 1 ? MinigunWeapon : default, MinigunOffset);
            Arm(upgrade, 2, rank >= 2 ? RocketWeapon : default, RocketOffset);
            Arm(upgrade, 3, rank >= 3 ? LaserWeapon : default, LaserOffset);
        }

        private void Arm(SentryWeaponUpgrade* upgrade, int slotIndex, AssetRef<WeaponDataAsset> weapon, FPVector3 offset)
        {
            upgrade->WeaponData[slotIndex] = weapon;
            upgrade->WeaponOffset[slotIndex] = offset;
            upgrade->Source[slotIndex] = weapon.IsValid ? this : default;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
