namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // The generic "was this target shoved into a wall, and if so punish it" step, shared by every
    // knockback source that wants a wall reaction. The architecture it completes is:
    //
    //     knockback source -> enemy movement -> valid wall impact -> wall-slam effect
    //
    // ...where each source only supplies its own knockback and push direction, and this owns the wall
    // half. Extracted verbatim from IronShoulderSkillAction's own private TryStunIfPushedIntoWall
    // (its behavior is unchanged) once Brute's Groundbreaker needed the same reaction from a
    // completely different source - a vertical landing rather than a dash - so there is exactly ONE
    // wall-impact implementation rather than one per Ascension.
    //
    // It also owns the PRESENTATION half of that same idea: it fires the generic WallSlammed event
    // itself rather than leaving each caller to raise its own, so any knockback source added later gets
    // the shared wall-impact VFX (see EffectsManager) with no extra hookup.
    //
    // The check is deliberately a SIMPLIFICATION rather than real multi-tick impulse tracking: it
    // raycasts a short distance along the push direction from the target's own position the instant
    // the knockback lands, and counts a wall found there as a slam. It reads as "shoved into the wall
    // right behind them" without needing to follow the target across ticks.
    //
    // (Concussive Impact's own landing reaction is deliberately NOT this - it genuinely tracks a
    // launched enemy across ticks via JuggernautLaunched/JuggernautLandingImpactSystem, because it has
    // to fire on the enemy's own landing, which may be many ticks later and anywhere. Two different
    // questions, two different mechanisms; see docs/brute-ascensions.md.)
    public static unsafe class WallSlamUtility
    {
        // Same HitStatics | HitKinematics combination every other wall probe in this codebase uses
        // (EnemyMovementUtility.IsBlockedByWall, JuggernautSkillData.ClampToWall) - HitStatics alone
        // lets level-chunk wall geometry pass through undetected.
        private const QueryOptions WallQueryOptions = QueryOptions.HitStatics | QueryOptions.HitKinematics;

        // Returns whether a wall was actually found in the push direction. `stunned` additionally
        // reports whether the Stun genuinely LANDED - those differ whenever the target is inside a
        // hard-CC immunity window or is a tier authored ImmuneToHardCC (see
        // EnemyTierResistanceConfig/StatusEffectUtility.ApplyStun), which is exactly the distinction a
        // caller needs when it gates a further reward on the stun rather than on the wall (Brute's
        // Groundbreaker rank 3 Exposed window). A caller that only cares about the wall - Iron
        // Shoulder's damage bonus - can ignore it.
        //
        // stunDuration <= 0 skips the Stun entirely and makes this a pure wall query.
        public static bool TryWallSlam(Frame f, EntityRef target, EntityRef owner, FPVector3 pushDirection,
            FP wallCheckDistance, FP stunDuration, out bool stunned)
        {
            stunned = false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return false;

            if (pushDirection.SqrMagnitude <= FP._0)
                return false;

            FPVector3 direction = pushDirection.Normalized;

            int wallMask = EnemyMovementUtility.GetGroundLayerMask(f);
            Hit3D? wallHit = f.Physics3D.Raycast(transform->Position, direction, wallCheckDistance, wallMask, WallQueryOptions);

            if (wallHit.HasValue == false)
                return false;

            if (stunDuration > FP._0)
            {
                stunned = StatusEffectUtility.ApplyStun(f, target, stunDuration, owner);
            }

            // CastDistanceNormalized (fraction of the query distance, 0-1) rather than hit.Point -
            // Point only reads real data when the query passes QueryOptions.ComputeDetailedInfo,
            // which this doesn't (same reasoning JuggernautSkillData.ClampToWall/
            // WeaponSystem.FireHitscan already document). Reporting the wall CONTACT point rather than
            // the target's own position matters here: the target is still short of the surface when
            // this resolves, so a directional burst would otherwise hang in the air in front of it.
            FPVector3 contactPoint = transform->Position + direction * (wallHit.Value.CastDistanceNormalized * wallCheckDistance);

            // Fired from here rather than from each caller, so every knockback source that routes
            // through this utility gets the same impact VFX for free - the presentation half of the
            // same "one shared wall-impact implementation" this class exists for. See WallSlammed in
            // Events.qtn.
            f.Events.WallSlammed(target, owner, contactPoint, direction, stunned);

            return true;
        }
    }
}
