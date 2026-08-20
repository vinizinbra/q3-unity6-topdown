namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Sentry Ascension (Overload Core, line 4/4) - the machine's death becomes offensive, so
    // losing a sentry stops being pure downside.
    //
    //  - Rank 1: it explodes when it expires or is destroyed.
    //  - Rank 2: much bigger, with strong knockback.
    //  - Rank 3 "Critical Meltdown": bigger still, and enemies caught become Exposed - taking more
    //    damage from EVERY source for a few seconds. Exposed reuses the pre-existing generic Rupture
    //    status (StatusEffectUtility.ApplyRupture / StatusEffects.RuptureDamageMultiplier), which is
    //    already "incoming damage multiplier with take-the-stronger semantics" - no new status was
    //    needed, and it composes with everything that already reads it.
    //
    // Damage is a percentage of Sentry Skill Damage, resolved to a flat number at deploy time, so it
    // scales with Lux's skill investment rather than falling off.
    //
    // It fires only when Health genuinely reaches 0 - decay or combat damage alike, which are the same
    // path by design (see Sentry.qtn). A sentry retired for HOUSEKEEPING (replaced past her active cap,
    // or picked up and moved by Relocation Protocol) is despawned with an explicit DespawnIntent, and
    // DamageUtility.TrySentryOverload skips those - so redeploy-spam can never become the optimal way
    // to trigger this.
    public unsafe partial class SentryOverloadCoreSkillAction : SkillActionData
    {
        [Tooltip("Percent of Sentry Skill Damage per rank. 1 = 100%.")]
        public FP[] DamagePercent = { FP._1, FP.FromString("1.75"), FP.FromString("2.50") };

        public FP BaseRadius = FP._4;
        public FP[] RadiusMultiplier = { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };

        [Tooltip("Rank 2+ - 0 skips the knockback sweep entirely.")]
        public FP[] KnockbackForce = { FP._0, 12, 12 };

        [Header("Rank 3 - Critical Meltdown")]
        [Tooltip("Extra damage taken by enemies caught in the blast. 0.20 = +20%.")]
        public FP[] ExposedDamageTakenBonus = { FP._0, FP._0, FP._0_20 };
        public FP ExposedDuration = FP._3;

        public SentryOverloadCoreSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override FP EffectRadius => BaseRadius;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<SentryOverloadUpgrade>(filter.Entity, out var upgrade);
            upgrade->Damage = DamagePercent[index] * LuxAscensionUtility.ResolveSentrySkillDamage(f, filter.Entity);
            upgrade->Radius = BaseRadius * RadiusMultiplier[index];
            upgrade->KnockbackForce = KnockbackForce[index];
            upgrade->ExposedDamageTakenBonus = ExposedDamageTakenBonus[index];
            upgrade->ExposedDuration = ExposedDuration;
            upgrade->Source = this;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
