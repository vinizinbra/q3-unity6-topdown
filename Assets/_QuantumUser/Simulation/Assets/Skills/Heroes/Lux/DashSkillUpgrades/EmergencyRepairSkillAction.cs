namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Dash Ascension (Emergency Repair, line 1/2) - Lux's dash becomes machine maintenance:
    // ending a dash next to one of her own Sentries services it.
    //
    //  - Rank 1: repairs a fraction of the Sentry's Max HP. Because a Sentry's lifetime IS its Health
    //    (it decays at Sentry.DecayRate - see Sentry.qtn), repairing it is literally giving it that
    //    much of its lifetime back, with no second timer to keep in sync.
    //  - Rank 2: also extends its remaining lifetime outright - drawn from that Sentry's OWN capped
    //    allowance (Sentry.LifetimeExtensionRemaining, seeded at deploy from
    //    SentryLifetimeExtensionBudget), which is what stops a dash-cooldown build from keeping one
    //    machine alive indefinitely. Each new Sentry gets a fresh allowance; no single one is immortal.
    //  - Rank 3 "Emergency Overclock": the repair also gives it a short Fire Rate burst.
    //
    // Scoped to sentries this Lux owns (SentryUtility.FindOwnedSentryNear), so a teammate Lux's
    // machine is never affected - the co-op ownership-isolation requirement.
    public unsafe partial class EmergencyRepairSkillAction : SkillActionData
    {
        [Tooltip("How close the dash has to END to one of her own Sentries.")]
        public FP Range = FP._6;

        [Tooltip("Fraction of the Sentry's Max HP restored - which, given the decay model, is also that fraction of its total lifetime.")]
        public FP[] RepairFraction = { FP.FromString("0.30"), FP.FromString("0.30"), FP.FromString("0.30") };

        [Header("Rank 2 - lifetime extension")]
        public FP[] LifetimeExtension = { FP._0, FP._2, FP._2 };

        [Tooltip("Total seconds of extension ANY single Sentry may ever receive, from this line and Relocation Protocol combined. Seeded onto each Sentry at deploy time.")]
        public FP MaxLifetimeExtensionPerSentry = FP._4;

        [Header("Rank 3 - Emergency Overclock")]
        public FP[] TempFireRateMultiplier = { FP._1, FP._1, FP._1_50 };
        public FP TempFireRateDuration = FP._2;

        public EmergencyRepairSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.End;
        }

        public override FP EffectRadius => Range;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            // Begin only publishes the per-sentry extension allowance, which SpawnSentrySkillAction
            // reads at DEPLOY time - a dash can't retroactively raise the budget of a machine that's
            // already out there. Take-the-larger so holding Relocation Protocol too doesn't stack two
            // budgets.
            if (firedPhase == SkillActionPhase.Begin)
            {
                if (LifetimeExtension[index] > FP._0)
                {
                    f.AddOrGet<SentryLifetimeExtensionBudget>(filter.Entity, out var budget);
                    budget->MaxPerSentry = FPMath.Max(budget->MaxPerSentry, MaxLifetimeExtensionPerSentry);
                }

                return;
            }

            EntityRef sentryEntity = SentryUtility.FindOwnedSentryNear(f, filter.Entity, filter.Transform3D->Position, Range);

            if (sentryEntity == EntityRef.None || f.Unsafe.TryGetPointer<Sentry>(sentryEntity, out var sentry) == false)
                return;

            SentryUtility.Repair(f, sentryEntity, RepairFraction[index]);

            if (LifetimeExtension[index] > FP._0)
            {
                SentryUtility.TryExtendLifetime(f, sentryEntity, sentry, LifetimeExtension[index]);
            }

            if (TempFireRateMultiplier[index] > FP._1)
            {
                SentryUtility.ApplyTempFireRate(sentry, TempFireRateMultiplier[index], TempFireRateDuration);
            }

            f.Events.SentryRepaired(filter.Entity, sentryEntity);

            Log.Debug($"[Skill] {filter.Entity} repaired sentry {sentryEntity} (rank {rank})");
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
