namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Ranked Dash Ascension (Iron Shoulder). Rank 1 is knockback-only by design, no damage of its
    // own - exactly the pre-ranking behavior, zero regression for an existing rank-1 pick. Rank 2+
    // adds direct-collision damage (DamagePercent of Juggernaut Skill Damage) plus a wall-slam damage
    // bonus; rank 3 additionally fires a radial damage+stun shockwave on a successful wall-slam. Push
    // is checked every Interval while the dash is active (OnGoing) plus once more on End - a short
    // box in front of the CURRENT position, oriented along the dash's own travel direction (not the
    // character's aim/facing - see the direction comment in Execute below), same "aura dragged along"
    // idea HitPathSkillAction.HitAroundCaster already uses for its own OnGoing mode. Replaces the old
    // approach of sweeping one box over the whole dash path: that box grew every tick and was
    // re-queried every tick, so anyone already hit near the dash's start kept getting re-shoved for
    // the rest of the dash - the "looks lagged" symptom. Knockback/damage (and the wall reaction that
    // rides on it) only ever lands once per activation per enemy - see IronShoulderHitTracker,
    // granted fresh on this action's own Begin phase. Elite/Boss enemies are no longer excluded - they
    // go through the same DamageUtility.ApplyKnockback call as everyone else, which already scales (or
    // fully resists) by the target's own tier resistance (StatusEffectUtility.GetTierResistance), so a
    // heavy target naturally shrugs off more of the shove without a separate hard skip here. The "pushed into a
    // wall" check is a simplification: rather than tracking the knockback impulse across ticks to see
    // where it actually lands, this raycasts a short distance in the push direction from the hit
    // point the instant it lands - if a wall is right there, it counts. Reads as "shoved into the
    // wall behind them" without needing multi-tick physics tracking. Rank 3's shockwave deliberately
    // calls BruteAscensionUtility.ApplyRadialStunDamage (a plain damage+stun sweep) rather than
    // another knockback/wall-check, so it can never recursively re-trigger the wall reaction itself.
    // Its damage naturally synergizes with Concussive Impact's own bonus vs Stunned targets (see
    // StunDamageBonusUpgrade) since it flows through the normal DamageUtility.ApplyDamage pipeline -
    // no extra code needed for that.
    public unsafe partial class IronShoulderSkillAction : SkillActionData
    {
        public FP Width = 2;
        public FP Height = 2;
        public FP Length = 1;
        public KnockbackTier KnockbackTier = KnockbackTier.Strong;
        public FP WallCheckDistance = 2;
        public FP StunDuration = 1;

        public FP[] DamagePercent = { FP._0, FP.FromString("0.60"), FP.FromString("0.60") };
        public FP[] WallSlamDamageBonus = { FP._0, FP._0_50, FP._0_50 };
        public FP[] ShockwaveRadius = { FP._0, FP._0, FP._3 };
        public FP[] ShockwaveDamagePercent = { FP._0, FP._0, FP.FromString("0.80") };

        // Must match IronShoulderHitTracker.HitEntities' own array size - the qtn side has no way to
        // reference this constant, so both have to be kept in sync by hand.
        private const int MaxTrackedHits = 8;

        public IronShoulderSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
            Interval = FP._0_10;
        }

        protected override object[] DescriptionArgs => new object[] { KnockbackTier };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            if (firedPhase == SkillActionPhase.Begin)
            {
                f.AddOrGet<IronShoulderHitTracker>(filter.Entity, out var tracker);
                tracker->HitCount = 0;
                return;
            }

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);
            if (config == null)
                return;

            config.GetKnockback(KnockbackTier, out FP force, out FP upwardForce);

            // The dash's own travel direction, not the character's facing - DashSkillData.Begin
            // resolves input-direction-first/Aim-angle-fallback once into TargetPosition and never
            // re-homes it mid-dash (see DashSkillData.ResolveDashDirection), so reconstructing it from
            // TargetPosition - StartPosition here reproduces that same direction without needing a
            // dedicated SkillSlot field. Matters because a dash's movement and the character's aim can
            // point different ways (e.g. dashing sideways while aiming elsewhere) - the shove should
            // follow where the dash physically shoved the player, not where they're looking.
            FPVector3 delta = slot->TargetPosition - slot->StartPosition;
            if (delta == FPVector3.Zero)
                return;

            FPVector3 direction = delta.Normalized;

            // Lifted to body height, same reasoning HitPathSkillAction/DashSkillData's own wall
            // check already use - a box centered on the ground plane sits half underground.
            FPVector3 center = filter.Transform3D->Position + direction * (Length / 2) + FPVector3.Up * (Height / 2);
            Shape3D box = Shape3D.CreateBox(new FPVector3(Width, Height, Length) / 2);
            FPQuaternion rotation = FPQuaternion.LookRotation(direction, FPVector3.Up);

            var hits = f.Physics3D.OverlapShape(center, rotation, box, -1, QueryOptions.HitAll);

            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;
            FP damage = DamagePercent[index] * BruteAscensionUtility.ResolveJuggernautSkillDamage(f, filter.Entity);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (f.Has<Enemy>(hitEntity) == false)
                    continue;

                if (TryMarkKnockedBack(f, filter.Entity, hitEntity) == false)
                    continue;

                DamageUtility.ApplyKnockback(f, hitEntity, direction, force, upwardForce, filter.Entity,
                    KnockbackApplyMode.Override);

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform))
                    f.Events.HitEffectApplied(filter.Entity, hitEntity, hitTransform->Position, true);

                bool hitWall = TryStunIfPushedIntoWall(f, filter.Entity, hitEntity, direction);

                if (damage > FP._0)
                {
                    FP finalDamage = hitWall == true ? damage * (FP._1 + WallSlamDamageBonus[index]) : damage;
                    DamageUtility.ApplyDamage(f, hitEntity, finalDamage, filter.Entity, DamageSource.Skill);
                }

                if (hitWall == true && ShockwaveRadius[index] > FP._0 && hitTransform != null)
                {
                    FP shockwaveDamage = ShockwaveDamagePercent[index] * BruteAscensionUtility.ResolveJuggernautSkillDamage(f, filter.Entity);
                    BruteAscensionUtility.ApplyRadialStunDamage(f, hitTransform->Position, ShockwaveRadius[index], filter.Entity, shockwaveDamage, FP._1);
                }
            }
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }

        // False if hitEntity was already knocked back earlier this activation (see
        // IronShoulderHitTracker) - this skill has nothing else to apply per hit, so a dedupe here
        // gates the knockback, its wall-stun, and the impact VFX all at once. Once the tracker's
        // capacity is full, any further new enemy just isn't deduped (falls back to pre-existing
        // repeat behavior).
        private static bool TryMarkKnockedBack(Frame f, EntityRef caster, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<IronShoulderHitTracker>(caster, out var tracker) == false)
                return true;

            for (int i = 0; i < tracker->HitCount; i++)
            {
                if (tracker->HitEntities[i] == target)
                    return false;
            }

            if (tracker->HitCount < MaxTrackedHits)
            {
                tracker->HitEntities[tracker->HitCount] = target;
                tracker->HitCount++;
            }

            return true;
        }

        // Returns whether hitEntity was pushed into a wall - callers use this to gate the wall-slam
        // damage bonus/shockwave (rank 2+/3) alongside the Stun that already rode on it at rank 1.
        private bool TryStunIfPushedIntoWall(Frame f, EntityRef owner, EntityRef hitEntity, FPVector3 direction)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var transform) == false)
                return false;

            int wallMask = EnemyMovementUtility.GetGroundLayerMask(f);
            const QueryOptions WallQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;
            Hit3D? wallHit = f.Physics3D.Raycast(transform->Position, direction, WallCheckDistance, wallMask, WallQueryOptions);

            if (wallHit.HasValue == true)
            {
                StatusEffectUtility.ApplyStun(f, hitEntity, StunDuration, owner);
            }

            return wallHit.HasValue;
        }
    }
}
