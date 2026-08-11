namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Kai's Undertow Ascension - see Heroes/Kai/Undertow.qtn/docs/kai-ascensions.md. Renamed from
    // KaiVoidwalkerMasterySystem now that Evasive Reflex (folded into Mirror Step rank 3), Phantom
    // Strike (now a genuine Dash-slot SkillActionData, granted from its own Execute instead of a
    // signal handler) and First Strike (still hooks directly into DamageUtility.ResolveOutgoingDamage,
    // same reasoning PixieDemolitionMasterySystem's own comment gives) all left this system - Undertow
    // is the only thing left that needs a manual per-tick Update body.
    [Preserve]
    public unsafe class KaiUndertowSystem : SystemMainThread, ISignalOnWeaponHitLanded
    {
        public override void Update(Frame f)
        {
            TickUndertowPulls(f);
        }

        // Drags a struck enemy toward whichever OTHER enemy is currently nearest to IT, recomputed
        // fresh every tick exactly like VortexSystem's own pull (DamageUtility.ApplyPull, never
        // ApplyKnockback - must not stagger the victim, same reasoning VortexSystem's class comment
        // gives). Registered after EnemySystem in SystemSetup.User.cs for the same reason VortexSystem
        // is - EnemySystem writes PhysicsBody3D.Velocity every tick regardless of phase, which would
        // otherwise erase this impulse before it moved anything. Rank 3 "Gravitational Bond" also
        // Binds the target for BoundDuration whenever a pull actually lands (see
        // StatusEffectUtility.ApplyBound) - ungated at ranks 1-2, where BoundDuration is 0 and
        // ApplyBound simply writes a 0 duration that's already expired.
        private static void TickUndertowPulls(Frame f)
        {
            var pulls = f.Filter<UndertowPull, Transform3D>();

            while (pulls.Next(out EntityRef entity, out UndertowPull pull, out Transform3D transform))
            {
                FP remaining = pull.Remaining - f.DeltaTime;

                if (f.Unsafe.TryGetPointer<UndertowPull>(entity, out var livePull) == false)
                    continue;

                if (remaining <= FP._0)
                {
                    f.Remove<UndertowPull>(entity);
                    continue;
                }

                livePull->Remaining = remaining;

                if (pull.Force <= FP._0)
                    continue;

                if (TryFindNearestOtherEnemy(f, entity, transform.Position, out EntityRef nearest, out FPVector3 nearestPosition) == false)
                {
                    livePull->LinkTarget = EntityRef.None;
                    continue;
                }

                // Fired here (not from OnWeaponHitLanded, where the pull target isn't resolved yet)
                // and only on an actual change - once when a fresh link forms, again if the nearest
                // enemy switches mid-pull, never every tick the same pairing holds. Both entities are
                // always genuine enemies - never Kai/the owner, which the visual feedback spec calls
                // for explicitly ("a cue on both affected enemies").
                if (nearest != pull.LinkTarget)
                {
                    f.Events.UndertowTriggered(entity, nearest);
                }

                livePull->LinkTarget = nearest;

                DamageUtility.ApplyPull(f, entity, nearestPosition - transform.Position, pull.Force);

                if (pull.BoundDuration > FP._0)
                {
                    StatusEffectUtility.ApplyBound(f, entity, pull.BoundDuration);
                }
            }
        }

        // Enemies only, nearest by flat distance not excluded by anything but Dead/Invulnerable -
        // same exclusions VortexSystem.TryFindNearestEnemy/EnemyMovementUtility.TryFindNearestEnemy
        // already use elsewhere. No radius cap (global search) - Undertow is meant to always find
        // "the crowd" regardless of how spread out the fight currently is; a lone last enemy simply
        // finds nothing and the pull no-ops for that tick.
        private static bool TryFindNearestOtherEnemy(Frame f, EntityRef exclude, FPVector3 origin, out EntityRef nearest, out FPVector3 nearestPosition)
        {
            nearest = EntityRef.None;
            nearestPosition = default;
            FP nearestSqrDistance = FP.MaxValue;

            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef candidate, out Enemy enemy, out Transform3D candidateTransform))
            {
                if (candidate == exclude || enemy.Phase == EnemyActionPhase.Dead)
                    continue;

                if (f.Has<Invulnerable>(candidate) == true)
                    continue;

                FP sqrDistance = (candidateTransform.Position - origin).SqrMagnitude;

                if (sqrDistance >= nearestSqrDistance)
                    continue;

                nearestSqrDistance = sqrDistance;
                nearest = candidate;
                nearestPosition = candidateTransform.Position;
            }

            return nearest != EntityRef.None;
        }

        // Refresh-only - a target already being pulled just gets its window/force re-armed by a fresh
        // hit, same idiom VoidFieldSystem.EnemyRefreshDuration already uses for its own slow. Rank 2's
        // HeavyTierMultiplier ("improved effectiveness against heavier enemy types") is baked into the
        // pull's own Force here, once, rather than re-read every tick - cheaper, and matches every
        // other Begin-baked upgrade's "resolved once at grant/refresh time" idiom. Does NOT fire
        // UndertowTriggered - the pull target (the second of the two enemies the visual feedback
        // needs) isn't known yet at hit time, only once TickUndertowPulls resolves it - see there.
        public void OnWeaponHitLanded(Frame f, EntityRef target, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<UndertowUpgrade>(owner, out var upgrade) == false)
                return;

            FP force = upgrade->PullForce;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == true)
            {
                EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

                if (data.Tier >= EnemyTier.Specialist)
                {
                    force *= upgrade->HeavyTierMultiplier;
                }
            }

            f.AddOrGet<UndertowPull>(target, out var pull);
            pull->Remaining = upgrade->PullDuration;
            pull->Force = force;
            pull->BoundDuration = upgrade->BoundDuration;
        }
    }
}
