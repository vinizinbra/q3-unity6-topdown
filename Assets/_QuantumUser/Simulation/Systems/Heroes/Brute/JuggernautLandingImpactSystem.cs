namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Watches every enemy JuggernautSkillData.Discharge marked with JuggernautLaunched (see
    // JuggernautLandingImpactUpgrade/JuggernautLandingRootUpgrade) for the moment it either touches
    // ground again (EnemyMovementUtility.IsGrounded - the same check
    // EnemySystem.TickKnockbackRecovery uses) or slams into a wall while still airborne
    // (CheckWallImpact, below), applying each equipped upgrade's own Damage and rolling its own
    // chance (StunChance/RootChance, independent of each other) right then, before removing the
    // marker.
    // GroundCheckDelay holds off the very first check for a short grace period - without it, this
    // could fire on the same tick the knockback impulse landed, before physics has actually
    // integrated it into an airborne position yet.
    [Preserve]
    public unsafe class JuggernautLandingImpactSystem : SystemMainThreadFilter<JuggernautLandingImpactSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Launched->GroundCheckDelay > FP._0)
            {
                filter.Launched->GroundCheckDelay -= f.DeltaTime;
                return;
            }

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            FPVector3 position = filter.Transform3D->Position;
            FP bodyRadius = EnemyMovementUtility.ResolveShapeRadius(filter.Collider->Shape);
            FPVector3 velocityXZ = new FPVector3(filter.Body->Velocity.X, FP._0, filter.Body->Velocity.Z);

            bool grounded = EnemyMovementUtility.IsGrounded(f, filter.Entity, position, groundLayerMask, out FP groundY);
            bool hitWall = grounded == false && CheckWallImpact(f, position, velocityXZ, bodyRadius, groundLayerMask);

            if (grounded == false && hitWall == false)
                return;

            EntityRef owner = filter.Launched->Owner;
            FP damage = filter.Launched->Damage;
            FP stunChance = filter.Launched->StunChance;
            FP stunDuration = filter.Launched->StunDuration;
            FP rootDamage = filter.Launched->RootDamage;
            FP rootChance = filter.Launched->RootChance;
            FP rootDuration = filter.Launched->RootDuration;
            AssetRef<JuggernautLandingImpactSkillAction> source = filter.Launched->Source;

            f.Remove<JuggernautLaunched>(filter.Entity);

            if (damage > FP._0)
            {
                DamageUtility.ApplyDamage(f, filter.Entity, damage, owner, DamageSource.Skill);
            }

            if (DamageUtility.RollChance(f, stunChance) == true)
            {
                StatusEffectUtility.ApplyStun(f, filter.Entity, stunDuration, owner);
            }

            // JuggernautLandingRootUpgrade - independent of the Stun upgrade above, own Damage/roll.
            if (rootDamage > FP._0)
            {
                DamageUtility.ApplyDamage(f, filter.Entity, rootDamage, owner, DamageSource.Skill);
            }

            if (DamageUtility.RollChance(f, rootChance) == true)
            {
                // Correct the position first so StatusEffectUtility.ApplyRoot - which fires the
                // generic EntityRooted view event off the entity's current Transform3D.Position -
                // reports the precise, already-snapped-to-ground position rather than the raw
                // (possibly still-penetrating/airborne) one.
                CorrectPosition(ref filter, grounded, groundY, velocityXZ);
                StatusEffectUtility.ApplyRoot(f, filter.Entity, rootDuration);
            }

            // Only fires the impact VFX event when the Stun upgrade actually baked a Source - a
            // Root-only launch has no asset to resolve BlastEffectPrefab from (EffectsManager would
            // hit a null asset on an unset AssetRef), so it's skipped rather than passed a default.
            if (source.IsValid == true)
            {
                f.Events.JuggernautLanded(filter.Entity, owner, position, bodyRadius, source);
            }

            Log.Debug($"[Skill] {filter.Entity} landed from a Juggernaut launch ({(hitWall == true ? "wall" : "ground")}) - damage {damage}, stun chance {stunChance}");
        }

        // Mirrors ChargeDeliveryData's own wall check exactly - raising the raycast origin above the
        // entity's raw pivot (WallCheckHeight) matters because a launched enemy's pivot is typically
        // at its feet, and a ray fired flat at ground level can skim under a wall collider that
        // doesn't extend all the way down. WallCheckDistance is a floor under the velocity-scaled
        // step distance, not just extra insurance: velocity can be small this tick even mid-launch
        // (arcing near the top of the knockback, or after air friction bleeds it down), and
        // velocity*deltaTime alone would then probe only a sliver of a unit, well short of an
        // adjacent wall.
        private static readonly FP WallCheckHeight = FP._1;
        private static readonly FP WallCheckDistance = FP._0_75;

        private static bool CheckWallImpact(Frame f, FPVector3 position, FPVector3 velocityXZ, FP bodyRadius, int groundLayerMask)
        {
            if (velocityXZ.SqrMagnitude <= FP._0)
                return false;

            FP probeDistance = FPMath.Max(velocityXZ.Magnitude * f.DeltaTime, WallCheckDistance) + bodyRadius + FP._0_10;
            FPVector3 wallCheckOrigin = position + FPVector3.Up * WallCheckHeight;

            return EnemyMovementUtility.IsBlockedByWall(f, wallCheckOrigin, velocityXZ, probeDistance, groundLayerMask);
        }

        // Root freezes the enemy kinematic exactly where it's standing the instant it procs (see
        // EnemySystem.Update) - since the ground/wall checks above only detect contact within a
        // small tolerance, the entity can be resting slightly inside the surface (foot below ground,
        // or overlapping the wall) rather than flush against it. Ground gets a precise fix (see
        // below); wall still gets the plain fixed nudge back along the way it was moving, since
        // there's no equivalent "hit point" captured for that check to compute a real penetration
        // depth from.
        private static readonly FP PositionCorrection = FP._0_20;

        // Rather than snapping flush to the ground (0% overlap, which can read as floating a hair
        // above it depending on the collider's own shape) or leaving whatever depth physics happened
        // to stop it at, this targets a fixed, deliberate overlap: GroundPenetrationRatio of the
        // collider's own full height (2x ResolveShapeHalfHeight - the pivot-to-bottom offset
        // IsGrounded's own probe already extends by) sits below groundY. groundY is the real ground
        // hit point from IsGrounded's own raycast, not this entity's current (possibly already
        // penetrating) Y - so the correction is computed from where the surface actually is, not
        // compounded on top of an already-wrong position.
        private static readonly FP GroundPenetrationRatio = FP._0_20 + FP._0_10;

        private static void CorrectPosition(ref Filter filter, bool grounded, FP groundY, FPVector3 velocityXZ)
        {
            if (grounded == true)
            {
                FP halfHeight = EnemyMovementUtility.ResolveShapeHalfHeight(filter.Collider->Shape);
                FP allowedPenetration = GroundPenetrationRatio * (halfHeight * 2);
                FP targetY = groundY + halfHeight - allowedPenetration;

                filter.Transform3D->Position = new FPVector3(filter.Transform3D->Position.X, targetY, filter.Transform3D->Position.Z);
            }
            else if (velocityXZ.SqrMagnitude > FP._0)
            {
                filter.Transform3D->Position -= velocityXZ.Normalized * PositionCorrection;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public PhysicsCollider3D* Collider;
            public PhysicsBody3D* Body;
            public JuggernautLaunched* Launched;
        }
    }
}
