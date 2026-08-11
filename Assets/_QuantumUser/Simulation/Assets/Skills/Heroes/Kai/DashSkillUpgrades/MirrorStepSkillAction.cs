namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Dash Ascension (Mirror Step, line 1/3) - see docs/kai-ascensions.md. Repurposes the old
    // (single-pick) ReflectProjectilesSkillAction - while dashing, any enemy-owned Projectile within
    // Radius of Kai's current position is sent back the way it came (reversed Velocity) and re-owned by
    // him, so it now damages enemies instead of players - same "flip Owner so damage resolves off the
    // new owner's stats" idiom DamageUtility.ApplyDamage already relies on everywhere. Runs every tick
    // of the dash (OnGoing) rather than a single swept-box test at Begin/End - a projectile keeps
    // moving throughout the dash, so a per-tick proximity check catches one that only entered range
    // mid-dash, which a before/after sweep would miss. Rank 2 widens the radius and scales up reflected
    // damage; rank 3 "Evasive Reflex" additionally refunds Vortex cooldown per successful reflection,
    // capped per Dash (see MirrorStepCooldownAccumulator, reset every Dash Begin).
    //
    // Excluded against Elite/Boss-owned projectiles (reflecting a boss's own attack back at it would
    // trivialize the fight) - same EnemyTier gate idiom used elsewhere in this roster (Pixie's Heavy
    // Payload, Kai's own Void Shards search).
    public unsafe partial class MirrorStepSkillAction : SkillActionData
    {
        public FP[] Radius = { FP._3, FP.FromString("4.50"), FP.FromString("4.50") };
        public FP[] ReflectedDamageMultiplier = { FP._1, FP._1_50, FP._1_50 };

        // Rank 3 "Evasive Reflex" only (0 at ranks 1-2, which leaves MirrorStepCooldownAccumulator
        // ungranted so no refund ever fires).
        public FP[] CooldownReductionPerReflect = { FP._0, FP._0, FP._0_50 };
        public FP MaxCooldownReductionPerDash = FP._2;

        public MirrorStepSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing;
        }

        protected override object[] DescriptionArgs => new object[] { Radius[0] };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (firedPhase == SkillActionPhase.Begin)
            {
                if (rank >= 3)
                {
                    f.AddOrGet<MirrorStepCooldownAccumulator>(filter.Entity, out var accumulator);
                    accumulator->AppliedThisDash = FP._0;
                    accumulator->MaxPerDash = MaxCooldownReductionPerDash;
                    accumulator->PerReflect = CooldownReductionPerReflect[index];
                }

                return;
            }

            FP radius = Radius[index];
            FP damageMultiplier = ReflectedDamageMultiplier[index];
            FPVector3 position = filter.Transform3D->Position;
            var projectiles = f.Filter<Projectile, Transform3D>();

            while (projectiles.Next(out EntityRef projectileEntity, out Projectile projectile, out Transform3D projectileTransform))
            {
                // Already-reflected projectiles are no longer Enemy-owned (see below), so this
                // naturally skips re-reflecting the same shot on a later tick of the same dash.
                if (f.Has<Enemy>(projectile.Owner) == false)
                    continue;

                if (IsExcludedTier(f, projectile.Owner) == true)
                    continue;

                if ((projectileTransform.Position - position).SqrMagnitude > radius * radius)
                    continue;

                if (f.Unsafe.TryGetPointer<Projectile>(projectileEntity, out var live) == false)
                    continue;

                live->Velocity = -live->Velocity;
                live->Owner = filter.Entity;
                live->Source = DamageSource.Skill;
                live->Damage *= damageMultiplier;

                f.Events.ProjectileReflected(filter.Entity, projectileTransform.Position);

                TryRefundCooldown(f, filter.Entity);

                Log.Debug($"[Skill] {filter.Entity} reflected {projectileEntity} back the way it came");
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }

        private static void TryRefundCooldown(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<MirrorStepCooldownAccumulator>(entity, out var accumulator) == false)
                return;

            FP remaining = accumulator->MaxPerDash - accumulator->AppliedThisDash;

            if (remaining <= FP._0)
                return;

            FP amount = FPMath.Min(accumulator->PerReflect, remaining);

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return;

            SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, amount);
            accumulator->AppliedThisDash += amount;
        }

        // Excluded against Elite/Boss enemies - same EnemyTier gate idiom used elsewhere in this
        // roster (Kai's own Void Shards search, Brute's old Iron Shoulder tier gate).
        private static bool IsExcludedTier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(owner, out var enemy) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return data.Tier >= EnemyTier.Elite;
        }
    }
}
