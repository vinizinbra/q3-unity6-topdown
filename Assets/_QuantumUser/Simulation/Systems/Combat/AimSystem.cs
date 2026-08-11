namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Runs after KCCSystem so it reads this tick's freshly-computed movement velocity.
    // Aim angle only updates while actually moving (horizontal speed above MinSpeed) - holds the
    // last direction while stationary instead of snapping back to a default facing. Doesn't touch
    // Transform3D.Rotation - the KCC entity itself never needs to yaw; view-side facing (sprite
    // flip/billboard) reads Aim.Angle instead.
    //
    // Target is the closest living Enemy within range, re-evaluated fresh every tick - no cone
    // gating. Distance is XZ-only (flat), so elevation differences (flying enemies, terrain
    // height) don't skew which one counts as closest. Range comes from the entity's equipped
    // Weapon.WeaponData.Range when it has one (players); entities without a Weapon (enemies
    // target players for facing purposes too) fall back to FallbackTargetRange.
    //
    // WeaponSystem calls NotifyFired on every shot, which locks Aim.Target in place
    // (Aim.LockedTarget/TargetSwitchTimer) for TargetLockDuration - otherwise a shot could land
    // ordered against one enemy while the visible reticle had already flipped to a closer one that
    // wandered into range mid-fight. The lock releases early if the locked target dies or leaves
    // range, so this never keeps aiming at something that's no longer a valid target.
    [Preserve]
    public unsafe class AimSystem : SystemMainThreadFilter<AimSystem.Filter>
    {
        private const string EnemyLayerName = "Enemy";

        private static readonly FP MinSpeed = FP._0_10;
        private static readonly FP FallbackTargetRange = 12;
        private static readonly FP TargetLockDuration = 1;

        // Matches the deadzone BlobAnimationView used to tune locally before FacingSign moved
        // here - keeps the facing flip from flickering while aiming near straight up/down.
        private static readonly FP FacingDeadzone = FP._1 / 5;

        private int? _enemyLayerMask;

        public override void Update(Frame f, ref Filter filter)
        {
            FPVector3 velocity = filter.KCC->Data.KinematicVelocity;
            FPVector3 horizontalVelocity = new FPVector3(velocity.X, FP._0, velocity.Z);

            if (horizontalVelocity.SqrMagnitude > MinSpeed * MinSpeed)
            {
                filter.Aim->Angle = FPMath.Atan2(horizontalVelocity.X, horizontalVelocity.Z) * FP.Rad2Deg;
            }

            FPVector3 selfPosition = filter.Transform3D->Position;
            EntityRef target = ResolveTarget(f, filter.Entity, filter.Aim, selfPosition);
            filter.Aim->Target = target;

            if (target != EntityRef.None && f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
            {
                FPVector3 delta = targetTransform->Position - selfPosition;
                filter.Aim->Angle = FPMath.Atan2(delta.X, delta.Z) * FP.Rad2Deg;
            }

            UpdateFacingSign(filter.Aim);
        }

        // Holds the last committed facing while the aim direction is too close to straight up/
        // down to tell left from right (the same ambiguity BlobAnimationView used to resolve on
        // its own) - see the FacingSign field comment in Aim.qtn for why this lives here instead
        // of being re-derived independently in sim and view.
        private static void UpdateFacingSign(Aim* aim)
        {
            FP dirX = FPMath.Sin(aim->Angle * FP.Deg2Rad);

            if (FPMath.Abs(dirX) > FacingDeadzone)
            {
                aim->FacingSign = FPMath.Sign(dirX);
            }
            else if (aim->FacingSign == FP._0)
            {
                aim->FacingSign = FP._1;
            }
        }

        // Called by WeaponSystem right after a shot lands - keeps Aim.Target pointed at whatever
        // was just fired at for TargetLockDuration instead of letting the very next tick's
        // closest-enemy re-evaluation flip it to something else.
        public static void NotifyFired(Aim* aim)
        {
            aim->LockedTarget = aim->Target;
            aim->TargetSwitchTimer = TargetLockDuration;
        }

        private EntityRef ResolveTarget(Frame f, EntityRef self, Aim* aim, FPVector3 origin)
        {
            if (aim->TargetSwitchTimer > FP._0)
            {
                aim->TargetSwitchTimer -= f.DeltaTime;

                if (IsAliveTarget(f, aim->LockedTarget, out var lockedTransform) == true &&
                    IsWithinRange(origin, lockedTransform->Position, GetTargetRange(f, self)) == true)
                {
                    return aim->LockedTarget;
                }

                // Locked target died or wandered out of range - no point holding the lock any
                // longer than that, so release it early instead of waiting out the timer.
                aim->TargetSwitchTimer = FP._0;
            }

            return FindClosestTarget(f, self, origin);
        }

        private static bool IsWithinRange(FPVector3 origin, FPVector3 targetPosition, FP range)
        {
            FPVector3 delta = targetPosition - origin;
            FP flatSqrDistance = delta.X * delta.X + delta.Z * delta.Z;

            return flatSqrDistance <= range * range;
        }

        private EntityRef FindClosestTarget(Frame f, EntityRef self, FPVector3 origin)
        {
            _enemyLayerMask ??= f.Layers.GetLayerMask(EnemyLayerName);

            Shape3D sphere = Shape3D.CreateSphere(GetTargetRange(f, self));
            var hits = f.Physics3D.OverlapShape(origin, FPQuaternion.Identity, sphere, _enemyLayerMask.Value, QueryOptions.HitAll);

            // Max's Vendetta - among otherwise-valid candidates, prefer whichever enemy already
            // carries this entity's own RevengeMark, closest first. A priority, not an exclusive
            // lock: falls through to the plain closest-overall pass below if nothing marked is in
            // range. Sits entirely inside this "otherwise-valid candidates" resolution, so the sticky
            // LockedTarget check in ResolveTarget (which runs before this is even called) still wins
            // outright - manual/sticky lock > Vendetta priority > normal closest, for free from call
            // order alone.
            EntityRef closestMarked = ResolveClosest(f, hits, origin, self, preferMarkedOnly: true);

            if (closestMarked != EntityRef.None)
                return closestMarked;

            return ResolveClosest(f, hits, origin, self, preferMarkedOnly: false);
        }

        private static EntityRef ResolveClosest(Frame f, HitCollection3D hits, FPVector3 origin, EntityRef self, bool preferMarkedOnly)
        {
            EntityRef closest = EntityRef.None;
            FP closestFlatSqrDistance = FP._0;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef entity = hits[i].Entity;

                if (IsAliveTarget(f, entity, out var transform) == false)
                    continue;

                if (preferMarkedOnly == true && IsMarkedBy(f, entity, self) == false)
                    continue;

                FPVector3 delta = transform->Position - origin;
                FP flatSqrDistance = delta.X * delta.X + delta.Z * delta.Z;

                if (closest == EntityRef.None || flatSqrDistance < closestFlatSqrDistance)
                {
                    closest = entity;
                    closestFlatSqrDistance = flatSqrDistance;
                }
            }

            return closest;
        }

        private static bool IsMarkedBy(Frame f, EntityRef entity, EntityRef self)
        {
            return f.Unsafe.TryGetPointer<RevengeMark>(entity, out var mark) == true && mark->MarkedBy == self;
        }

        private static FP GetTargetRange(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true)
            {
                // Effective range (base Range * RangeMultiplier), NOT raw weaponData.Range - so target
                // acquisition reaches as far as the weapon actually fires (WeaponSystem/ResolveWeaponRange
                // use the same formula). Reading base Range alone meant Weapon Range global upgrades and
                // the Long Barrel perk extended the shots but never let auto-aim lock onto anything past
                // the un-upgraded range.
                return WeaponPerkUtility.ResolveWeaponRange(f, weapon);
            }

            return FallbackTargetRange;
        }

        // Dead enemies linger (DamageUtility.ApplyDamage) for their death animation instead of
        // being destroyed immediately, so they'd otherwise still show up in the overlap query.
        // Invulnerable (e.g. a burrowed enemy - see BurrowDeliveryData) is excluded too - nothing
        // to gain by aiming/locking at a target every hit on it is already ignored by.
        private static bool IsAliveTarget(Frame f, EntityRef entity, out Transform3D* transform)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out transform) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == true && enemy->Phase == EnemyActionPhase.Dead)
                return false;

            if (f.Has<Invulnerable>(entity) == true)
                return false;

            return true;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public KCC* KCC;
            public Aim* Aim;
        }
    }
}
