namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Dash Ascension (Iron Shoulder) - knockback-only by design, no damage of its own - pushes
    // enemies in front of the player, checked every Interval while the dash is active (OnGoing) plus
    // once more on End - a short box in front of the CURRENT position, oriented along the dash's own
    // travel direction (not the character's aim/facing - see the direction comment in Execute
    // below), same "aura dragged along" idea HitPathSkillAction.HitAroundCaster already uses for its
    // own OnGoing mode. Replaces the old approach of sweeping one box over the whole dash path: that
    // box grew every tick and was re-queried every tick, so anyone already hit near the dash's start
    // kept getting re-shoved for the rest of the dash - the "looks lagged" symptom. Knockback (and
    // the wall-stun that rides on it) only ever lands once per activation per enemy - see
    // IronShoulderHitTracker, granted fresh on this action's own Begin phase. Elite/Boss enemies are
    // excluded entirely - same EnemyTier gate idiom used elsewhere in this roster (Kai's Reflect
    // Projectiles/Void Pressure) - a shoulder charge shouldn't be able to shove around something that
    // heavy. The "pushed into a wall" check is a simplification: rather than tracking the knockback
    // impulse across ticks to see where it actually lands, this raycasts a short distance in the push
    // direction from the hit point the instant it lands - if a wall is right there, it counts. Reads
    // as "shoved into the wall behind them" without needing multi-tick physics tracking.
    public unsafe partial class IronShoulderSkillAction : SkillActionData
    {
        public FP Width = 2;
        public FP Height = 2;
        public FP Length = 1;
        public KnockbackTier KnockbackTier = KnockbackTier.Medium;
        public FP WallCheckDistance = 2;
        public FP StunDuration = 1;

        // Must match IronShoulderHitTracker.HitEntities' own array size - the qtn side has no way to
        // reference this constant, so both have to be kept in sync by hand.
        private const int MaxTrackedHits = 8;

        public IronShoulderSkillAction()
        {
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
            Interval = FP._0_10;
        }

        protected override object[] DescriptionArgs => new object[] { KnockbackTier };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
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

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef hitEntity = hits[i].Entity;

                if (f.Has<Enemy>(hitEntity) == false || IsExcludedTier(f, hitEntity) == true)
                    continue;

                if (TryMarkKnockedBack(f, filter.Entity, hitEntity) == false)
                    continue;

                DamageUtility.ApplyKnockback(f, hitEntity, direction, force, upwardForce, filter.Entity,
                    KnockbackApplyMode.Override);

                if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var hitTransform))
                    f.Events.HitEffectApplied(filter.Entity, hitEntity, hitTransform->Position, true);

                TryStunIfPushedIntoWall(f, filter.Entity, hitEntity, direction);
            }
        }

        // Excluded against Elite/Boss enemies - same EnemyTier gate idiom used elsewhere in this
        // roster (Kai's Reflect Projectiles/Void Pressure).
        private static bool IsExcludedTier(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return data.Tier >= EnemyTier.Elite;
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

        private void TryStunIfPushedIntoWall(Frame f, EntityRef owner, EntityRef hitEntity, FPVector3 direction)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(hitEntity, out var transform) == false)
                return;

            int wallMask = EnemyMovementUtility.GetGroundLayerMask(f);
            const QueryOptions WallQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;
            Hit3D? wallHit = f.Physics3D.Raycast(transform->Position, direction, WallCheckDistance, wallMask, WallQueryOptions);

            if (wallHit.HasValue == true)
            {
                StatusEffectUtility.ApplyStun(f, hitEntity, StunDuration, owner);
            }
        }
    }
}
