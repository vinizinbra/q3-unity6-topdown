namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Brute's Groundbreaker Passive Ascension - see Groundbreaker.qtn. Reacts to the generic
    // OnPlayerLanded signal (PlayerMovement.qtn/AutoJumpSystem): when Brute lands from a high enough
    // drop, everything nearby is thrown outward, anything shoved into a wall is Stunned, and (rank 3)
    // anything actually wall-stunned is left Exposed for a burst window.
    //
    // Unfiltered - no Filter query, the entity comes straight off the signal payload, same shape
    // MaxVendettaSystem/WeaponPerkReactionSystem already use. Scoped purely by GroundbreakerUpgrade's
    // presence, never an "is this entity Brute" check.
    //
    // NO INTERACTION WITH CONCUSSIVE IMPACT, by construction rather than by a guard. That line reacts
    // to an ENEMY's own landing after being launched by Discharge (JuggernautLaunched, stamped only by
    // JuggernautSkillData.Discharge and consumed by JuggernautLandingImpactSystem). This reacts to
    // BRUTE's own landing. Different trigger, different entity, and Groundbreaker never stamps
    // JuggernautLaunched - so the same landing can never satisfy both, and neither can feed the other.
    //
    // Fully deterministic: the simulation decides whether it triggers, who is caught, the knockback
    // direction, the wall result, the Stun and the Exposed window. The View only ever renders the
    // GroundbreakerSlammed event.
    [Preserve]
    public unsafe class BruteGroundbreakerSystem : SystemMainThread, ISignalOnPlayerLanded
    {
        public override void Update(Frame f)
        {
        }

        public void OnPlayerLanded(Frame f, EntityRef entity, FP fallDistance, LandingSource source)
        {
            if (f.Unsafe.TryGetPointer<GroundbreakerUpgrade>(entity, out var groundbreaker) == false)
                return;

            // The height gate. Everything the brief rules out (ordinary movement, a same-height dash,
            // tiny elevation changes, walking down a step or slope) reports a fall distance at or near
            // zero and fails here - no special-casing of any of them is needed, and none of it is tied
            // to map tiles or terrain tiers.
            if (fallDistance < groundbreaker->MinimumFallHeight)
                return;

            if (IsLandingSourceAllowed(groundbreaker->AllowedLandingSources, source) == false)
                return;

            if (groundbreaker->ImpactRadius <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
                return;

            FPVector3 center = transform->Position;

            // Skill Area - see StatUtility.GetAreaMultiplier. Resolved once here so the overlap, the
            // per-enemy loop and the GroundbreakerSlammed event all report the same real radius.
            FP impactRadius = groundbreaker->ImpactRadius * StatUtility.GetAreaMultiplier(f, entity);

            // Resolved once per landing, not per target - neither depends on which enemy is caught.
            FP damage = groundbreaker->ImpactDamagePercent * BruteAscensionUtility.ResolveJuggernautSkillDamage(f, entity);

            Shape3D sphere = Shape3D.CreateSphere(impactRadius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            int caught = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false || IsTierAffected(f, target, groundbreaker->MaxAffectedTierIndex) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                // Directly away from the landing point, flattened to the ground plane - the brief's
                // "pushed directly away from Brute's landing position". Flattened because a vertical
                // component would launch enemies upward off a shared floor rather than outward into
                // the walls the whole line is about; KnockbackUpwardForce is the authored, separate
                // lever for however much lift is actually wanted.
                FPVector3 delta = targetTransform->Position - center;
                FPVector3 flat = new FPVector3(delta.X, FP._0, delta.Z);

                // Degenerate case - an enemy standing exactly on the landing point has no outward
                // direction to resolve. Falls back to world-forward purely so it still gets thrown
                // somewhere rather than silently not being pushed at all.
                FPVector3 direction = flat.SqrMagnitude > FP._0 ? flat.Normalized : FPVector3.Forward;

                caught++;

                DamageUtility.ApplyKnockback(f, target, direction, groundbreaker->KnockbackForce,
                    groundbreaker->KnockbackUpwardForce, entity, KnockbackApplyMode.Override);

                if (damage > FP._0)
                {
                    DamageUtility.ApplyDamage(f, target, damage, entity, DamageSource.Skill);
                }

                // A Filler/Normal enemy is destroyed immediately on death (see
                // DamageUtility.ApplyDamage) - nothing left to wall-slam if that hit finished it.
                if (f.Exists(target) == false)
                    continue;

                TryWallSlam(f, entity, target, direction, groundbreaker);
            }

            f.Events.GroundbreakerSlammed(entity, center, impactRadius);

            Log.Debug($"[Skill] {entity} Groundbreaker landed from {fallDistance} ({source}) at {center} - " +
                      $"radius {impactRadius}, caught {caught}");
        }

        // Rank 2's wall Stun and rank 3's Exposed window. Uses the shared, hero-agnostic
        // WallSlamUtility - the same implementation Iron Shoulder's dash slam uses - so Groundbreaker
        // supplies only a different knockback source, never a second wall-collision system.
        private static void TryWallSlam(Frame f, EntityRef owner, EntityRef target, FPVector3 direction,
            GroundbreakerUpgrade* groundbreaker)
        {
            if (groundbreaker->WallStunEnabled == false)
                return;

            WallSlamUtility.TryWallSlam(f, target, owner, direction, groundbreaker->WallCheckDistance,
                groundbreaker->WallStunDuration, out bool stunned);

            // Gated on the Stun genuinely LANDING, not merely on a wall being there - so an enemy
            // inside a hard-CC immunity window, or a tier authored ImmuneToHardCC, correctly gets
            // neither half. This is also the whole "damage window" rule: Exposed is never applied to
            // everyone caught in the shockwave, only to whoever the landing actually slammed into a
            // wall, so the reward is for positioning and knockback angle rather than for landing near
            // a crowd.
            if (stunned == false || groundbreaker->VulnerabilityEnabled == false)
                return;

            // Reuses the pre-existing generic Rupture status (an incoming-damage multiplier with
            // take-the-stronger semantics), exactly as Lux's Overload Core rank 3 "Exposed" does - no
            // Brute-specific status, and it composes with everything that already reads Rupture.
            StatusEffectUtility.ApplyRupture(f, target, groundbreaker->VulnerabilityDuration,
                FP._1 + groundbreaker->VulnerabilityDamageTakenModifier);
        }

        // Bitmask over LandingSource - see GroundbreakerUpgrade.AllowedLandingSources. A 0 mask is
        // read as "everything", so an unauthored/legacy asset behaves permissively rather than
        // silently disabling the whole Ascension.
        private static bool IsLandingSourceAllowed(byte mask, LandingSource source)
        {
            if (mask == 0)
                return true;

            return (mask & (1 << (byte)source)) != 0;
        }

        private static bool IsTierAffected(Frame f, EntityRef target, byte maxAffectedTierIndex)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return (byte)data.Tier <= maxAffectedTierIndex;
        }
    }
}
