namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Dash line 9 (replaces the old standalone Reloading Slide) - restores a fraction of the current
    // magazine after dashing (same direct Weapon->Ammo top-up its predecessor used, just per-rank
    // now), plus a timed Fire Rate window via the existing Haste mechanism (already exactly "temporary
    // attack-speed multiplier, stacks by independent source, refresh in place" - no new primitive
    // needed, filter.Entity is its own Haste source same as every other self-buff). Rank 2
    // additionally layers a timed Weapon Damage buff (StatusEffectUtility.ApplyTemporaryWeaponDamage).
    // Rank 3 additionally opens a brief window where firing doesn't consume Ammo at all
    // (StatusEffects.NoAmmoConsumptionRemaining, checked directly by WeaponSystem).
    public unsafe partial class RunAndGunSkillAction : SkillActionData
    {
        public FP[] AmmoRestoreFraction = { FP._0_50, FP._1, FP._1 };
        public FP[] FireRateBonus = { FP._0_20, FP.FromString("0.30"), FP.FromString("0.40") };
        public FP HasteDuration = 2;

        [Header("Rank 2")]
        public FP WeaponDamageBonusDuration = 2;
        public FP WeaponDamageBonus = FP.FromString("0.15");

        [Header("Rank 3")]
        public FP NoAmmoConsumptionDuration = 2;

        public RunAndGunSkillAction()
        {
            Phase = SkillActionPhase.End;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (f.Unsafe.TryGetPointer<Weapon>(filter.Entity, out var weapon) == true)
            {
                int restoreAmount = FPMath.CeilToInt(weapon->MagazineSize * AmmoRestoreFraction[index]);
                int newAmmo = weapon->Ammo + restoreAmount;
                weapon->Ammo = newAmmo > weapon->MagazineSize ? weapon->MagazineSize : newAmmo;

                // Bypasses WeaponSystem's own reload path entirely, so its own WeaponReloaded fire
                // never happens for this top-up - fired directly here instead so this reads
                // identically to a normal reload.
                f.Events.WeaponReloaded(filter.Entity);
            }

            // ApplyHaste takes an ABSOLUTE multiplier (StatUtility.GetFireCooldown divides by it),
            // not a bonus fraction - same FP._1 + bonus conversion AllyBuffEffectData already does.
            StatusEffectUtility.ApplyHaste(f, filter.Entity, filter.Entity, HasteDuration, FP._1 + FireRateBonus[index]);

            if (rank >= 2)
            {
                StatusEffectUtility.ApplyTemporaryWeaponDamage(f, filter.Entity, WeaponDamageBonusDuration, WeaponDamageBonus);
            }

            if (rank >= 3 && f.Unsafe.TryGetPointer<StatusEffects>(filter.Entity, out var status) == true)
            {
                status->NoAmmoConsumptionRemaining = NoAmmoConsumptionDuration;
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
