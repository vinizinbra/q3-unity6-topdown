namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Watches every enemy JuggernautSkillData.Discharge marked with JuggernautLaunched (Concussive
    // Impact Ascension) for the moment it either touches ground again (EnemyMovementUtility.
    // IsGrounded - the same check EnemySystem.TickKnockbackRecovery uses) or slams into a wall while
    // still airborne (CheckWallImpact, below), applying landing Damage/StunDuration and, at rank 3
    // (ShockwaveRadius > 0), a further radial damage+stun pulse, before removing the marker.
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

            bool grounded = EnemyMovementUtility.IsGrounded(f, filter.Entity, position, groundLayerMask, out _);
            bool hitWall = grounded == false && CheckWallImpact(f, position, velocityXZ, bodyRadius, groundLayerMask);

            if (grounded == false && hitWall == false)
                return;

            EntityRef owner = filter.Launched->Owner;
            FP damage = filter.Launched->Damage;
            FP stunChance = filter.Launched->StunChance;
            FP stunDuration = filter.Launched->StunDuration;
            // Skill Area - resolved off the OWNER (the Brute who launched this enemy), never off the
            // enemy itself, which has no CharacterStats to read a multiplier from. Same funnel every
            // other Juggernaut radius already uses (see JuggernautSkillData's own Discharge/Aftershock).
            FP shockwaveRadius = filter.Launched->ShockwaveRadius * StatUtility.GetAreaMultiplier(f, owner);
            FP shockwaveDamagePercent = filter.Launched->ShockwaveDamagePercent;
            FP shockwaveStunDuration = filter.Launched->ShockwaveStunDuration;
            AssetRef<ConcussiveImpactSkillAction> source = filter.Launched->Source;

            f.Remove<JuggernautLaunched>(filter.Entity);

            if (damage > FP._0)
            {
                DamageUtility.ApplyDamage(f, filter.Entity, damage, owner, DamageSource.Skill);
            }

            if (DamageUtility.RollChance(f, stunChance) == true)
            {
                StatusEffectUtility.ApplyStun(f, filter.Entity, stunDuration, owner);
            }

            // Concussive Impact rank 3 - a further radial damage+stun pulse centered on the landed
            // enemy, scaled off the same "Juggernaut Skill Damage" baseline as the landing hit itself.
            if (shockwaveRadius > FP._0)
            {
                FP shockwaveDamage = shockwaveDamagePercent * BruteAscensionUtility.ResolveJuggernautSkillDamage(f, owner);
                BruteAscensionUtility.ApplyRadialStunDamage(f, position, shockwaveRadius, owner, shockwaveDamage, shockwaveStunDuration);
            }

            // Only fires the impact VFX event when the upgrade actually baked a Source.
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
