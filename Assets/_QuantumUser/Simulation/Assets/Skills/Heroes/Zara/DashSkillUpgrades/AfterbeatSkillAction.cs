namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // Ranked Dash Ascension (Afterbeat, line 1/2 on Dash) - Zara's dash feeds her Resonance clock and
    // then leaves rhythm behind her.
    //
    //  - Rank 1 "Quick Tempo": dashing grants a flat chunk of Resonance, and passing THROUGH enemies
    //    during the dash grants more per enemy (capped per dash, deduped per enemy).
    //  - Rank 2 "Afterbeat": about a second after the dash, a damaging/knocking pulse lands at the
    //    dash's own starting position.
    //  - Rank 3 "Double Beat": a second identical pulse at the dash's ENDING position too, and enemies
    //    caught by either pulse also generate Resonance - drawing on the same shared per-dash
    //    allowance rank 1's sweep uses, so the two can't compound past the cap.
    //
    // Countdowns/positions live on ZaraAfterbeat, ticked by ZaraAfterbeatSystem - no EntityPrototype
    // authoring needed, since neither pulse has a physical presence between scheduling and firing.
    public unsafe partial class AfterbeatSkillAction : SkillActionData
    {
        [Header("Rank 1 - Quick Tempo")]
        [Tooltip("Flat Resonance granted the moment the dash starts.")]
        public FP ResonanceOnDash = 20;

        [Tooltip("Radius around Zara checked every dash tick for enemies passed through.")]
        public FP SweepRadius = FP._1_50;

        [Tooltip("Resonance per enemy the dash passes through, and per enemy an Afterbeat pulse catches at rank 3. One shared per-dash cap covers both.")]
        public FP ResonancePerEnemyHit = 10;
        public FP MaxResonancePerDash = 40;

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
                SweepForResonance(f, filter.Entity, filter.Transform3D->Position, afterbeat);
                return;
            }

            if (firedPhase == SkillActionPhase.Begin)
            {
                // Quick Tempo - a flat amount, so it reads as a fixed, predictable contribution to the
                // Resonance clock rather than silently rescaling if Resonance.Max is ever retuned.
                ResonanceUtility.Grant(f, filter.Entity, ResonanceOnDash);

                afterbeat->ResonanceGrantedThisDash = FP._0;
                afterbeat->ResonancePerEnemyHit = ResonancePerEnemyHit;
                afterbeat->MaxResonancePerDash = MaxResonancePerDash;
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

        // Rank 1's "passing through enemies during the Dash grants additional Resonance" - deals no
        // damage of its own, this is purely a Resonance faucet. Deduped per enemy per dash
        // (ZaraAfterbeat.SweptEnemies) so standing inside the sweep radius doesn't pay every tick, and
        // capped by the same shared per-dash allowance rank 3's pulse hits use.
        private void SweepForResonance(Frame f, EntityRef owner, FPVector3 position, ZaraAfterbeat* afterbeat)
        {
            if (afterbeat->ResonancePerEnemyHit <= FP._0 || SweepRadius <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(SweepRadius);
            var hits = f.Physics3D.OverlapShape(position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false || AlreadySwept(afterbeat, target) == true)
                    continue;

                MarkSwept(afterbeat, target);
                ZaraAfterbeatSystem.GrantCappedResonance(f, owner, afterbeat);
            }
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
