namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // Ranked Dash Ascension (Afterbeat, line 1/2 on Dash) - Zara's dash feeds her Flow and then leaves
    // rhythm behind her. Migrated wholesale from Resonance; the line's identity as her Dash/movement
    // line is unchanged, only the resource it pays into.
    //
    //  - Rank 1 "Quick Tempo": dashing immediately pushes the Flow bar up a chunk, and each UNIQUE
    //    enemy passed THROUGH during the dash pushes it further (capped per dash, deduped per enemy).
    //  - Rank 2 "Afterbeat": about a second after the dash, a damaging/knocking pulse lands at the
    //    dash's own starting position.
    //  - Rank 3 "Double Beat": a second identical pulse at the dash's ENDING position too, and landing
    //    EITHER pulse on at least one enemy pushes the bar again - at most once per dash across both
    //    pulses, never once per enemy.
    //
    // Countdowns/positions live on ZaraAfterbeat, ticked by ZaraAfterbeatSystem - no EntityPrototype
    // authoring needed, since neither pulse has a physical presence between scheduling and firing.
    public unsafe partial class AfterbeatSkillAction : SkillActionData
    {
        [Header("Rank 1 - Quick Tempo")]
        [Tooltip("Fraction of the Flow bar granted the moment the dash starts (0.35 = a third of a bar). Clamped at full like every other grant, so this can never overfill. Dash is the most reliable way to keep the rhythm going, which is what pays for its cooldown.")]
        public FP FlowProgressOnDash = FP.FromString("0.35");

        [Tooltip("Radius around Zara checked every dash tick for enemies passed through.")]
        public FP SweepRadius = FP._1_50;

        [Tooltip("Fraction of the Flow bar granted per UNIQUE enemy the dash passes through (0.10 = a tenth of a bar each). An accelerant on top of the flat on-dash grant - dashing through a crowd fills faster than dashing through empty space.")]
        public FP ProgressPerEnemyHit = FP._0_10;

        [Tooltip("Cap on the per-enemy fraction above, per dash - so a dash through a dense pack converges instead of scaling with pack size.")]
        public FP MaxProgressPerDash = FP.FromString("0.40");

        [Header("Rank 2+ - the delayed pulse")]
        public FP Delay = FP._1;

        // Index 0 (rank 1) is never read - rank 1 has no pulse at all.
        public FP[] DamagePercentOfSkill = { FP._0, FP.FromString("0.75"), FP.FromString("0.75") };
        public FP[] Radius = { FP._0, FP._4, FP._4 };
        public FP[] KnockbackForce = { FP._0, FP._6, FP._6 };

        public AfterbeatSkillAction()
        {
            // OnGoing is rank 1's dash sweep - a dash covers ground over several ticks, so a single
            // before/after test would miss an enemy passed through mid-dash. Interval 0 = every tick.
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
            Interval = 0;
        }

        public override FP EffectRadius => Radius[Radius.Length - 1];

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<ZaraAfterbeat>(filter.Entity, out var afterbeat);

            if (firedPhase == SkillActionPhase.OnGoing)
            {
                SweepForFlowProgress(f, filter.Entity, filter.Transform3D->Position, afterbeat);
                return;
            }

            if (firedPhase == SkillActionPhase.Begin)
            {
                // Quick Tempo - a solid chunk of the bar up front, which is what makes Dash the single
                // most reliable way to keep the rhythm going and pays for its cooldown on its own.
                if (FlowProgressOnDash > FP._0 && f.Unsafe.TryGetPointer<ZaraFlow>(filter.Entity, out var flow) == true)
                {
                    ZaraFlowUtility.AddProgress(f, filter.Entity, flow, FlowProgressOnDash);
                }

                // Per-dash allowances all reset here, which is what makes every cap below genuinely
                // "per dash" rather than per activation of the ascension.
                afterbeat->ProgressThisDash = FP._0;
                afterbeat->ProgressPerEnemyHit = ProgressPerEnemyHit;
                afterbeat->MaxProgressPerDash = MaxProgressPerDash;
                afterbeat->GrantsFlowOnPulseHit = rank >= 3;
                afterbeat->FlowGrantedThisDash = false;
                afterbeat->SweptCount = 0;

                if (rank < 2)
                    return;

                afterbeat->StartRemaining = Delay;
                afterbeat->StartPosition = slot->StartPosition;
                afterbeat->StartDamage = ZaraAscensionUtility.ResolveHeroSkillDamage(f, filter.Entity) * DamagePercentOfSkill[index];
                // Skill Area (CharacterStats.AreaRadiusMultiplier) - see StatUtility.GetAreaMultiplier.
                afterbeat->StartRadius = Radius[index] * StatUtility.GetAreaMultiplier(f, filter.Entity);
                afterbeat->StartKnockbackForce = KnockbackForce[index];
                return;
            }

            if (rank < 3)
                return;

            afterbeat->EndRemaining = Delay;
            afterbeat->EndPosition = filter.Transform3D->Position;
            afterbeat->EndDamage = ZaraAscensionUtility.ResolveHeroSkillDamage(f, filter.Entity) * DamagePercentOfSkill[index];
            afterbeat->EndRadius = Radius[index] * StatUtility.GetAreaMultiplier(f, filter.Entity);
            afterbeat->EndKnockbackForce = KnockbackForce[index];
        }

        // Rank 1's "passing through enemies during the Dash fills the bar faster" - deals no damage of
        // its own, this is purely an accelerant. Deduped per enemy per dash (ZaraAfterbeat.SweptEnemies)
        // so lingering inside the sweep radius doesn't pay every tick, and capped per dash so a crowded
        // dash converges instead of scaling with pack size.
        private void SweepForFlowProgress(Frame f, EntityRef owner, FPVector3 position, ZaraAfterbeat* afterbeat)
        {
            if (afterbeat->ProgressPerEnemyHit <= FP._0 || SweepRadius <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<ZaraFlow>(owner, out var flow) == false)
                return;

            Shape3D sphere = Shape3D.CreateSphere(SweepRadius);
            var hits = f.Physics3D.OverlapShape(position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false || AlreadySwept(afterbeat, target) == true)
                    continue;

                MarkSwept(afterbeat, target);
                GrantSweepProgress(f, owner, afterbeat, flow);
            }
        }

        // Spends from one shared per-dash allowance, so a crowded dash converges on the cap instead of
        // scaling with pack size. Routed through AddProgress like every other write, so it can activate
        // Flow (and fire Headliner's Hype) mid-dash exactly as movement would.
        private static void GrantSweepProgress(Frame f, EntityRef owner, ZaraAfterbeat* afterbeat, ZaraFlow* flow)
        {
            FP remaining = afterbeat->MaxProgressPerDash - afterbeat->ProgressThisDash;

            if (remaining <= FP._0)
                return;

            FP granted = FPMath.Min(afterbeat->ProgressPerEnemyHit, remaining);

            afterbeat->ProgressThisDash += granted;
            ZaraFlowUtility.AddProgress(f, owner, flow, granted);
        }

        private static bool AlreadySwept(ZaraAfterbeat* afterbeat, EntityRef target)
        {
            for (int i = 0; i < afterbeat->SweptCount; i++)
            {
                if (afterbeat->SweptEnemies[i] == target)
                    return true;
            }

            return false;
        }

        private static void MarkSwept(ZaraAfterbeat* afterbeat, EntityRef target)
        {
            if (afterbeat->SweptCount >= afterbeat->SweptEnemies.Length)
                return;

            afterbeat->SweptEnemies[afterbeat->SweptCount] = target;
            afterbeat->SweptCount++;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
