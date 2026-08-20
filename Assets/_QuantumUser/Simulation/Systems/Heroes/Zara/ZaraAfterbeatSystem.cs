namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Ticks down ZaraAfterbeat's two independent countdowns (Start/End - see ZaraAfterbeat.qtn, set
    // by AfterbeatSkillAction) and fires each delayed pulse once it hits 0 - same "countdown
    // component ticked by its own tiny System" shape as ExplodeOnDeathTimerSystem/
    // JuggernautDischargeCooldownSystem, just two independent slots on one component instead of one.
    [Preserve]
    public unsafe class ZaraAfterbeatSystem : SystemMainThreadFilter<ZaraAfterbeatSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            TickPulse(f, filter.Entity, filter.Afterbeat, isStart: true);
            TickPulse(f, filter.Entity, filter.Afterbeat, isStart: false);
        }

        private static void TickPulse(Frame f, EntityRef owner, ZaraAfterbeat* afterbeat, bool isStart)
        {
            FP remaining = isStart ? afterbeat->StartRemaining : afterbeat->EndRemaining;

            if (remaining <= FP._0)
                return;

            remaining -= f.DeltaTime;

            if (isStart)
            {
                afterbeat->StartRemaining = remaining;
            }
            else
            {
                afterbeat->EndRemaining = remaining;
            }

            if (remaining > FP._0)
                return;

            if (isStart)
            {
                Fire(f, owner, afterbeat, afterbeat->StartPosition, afterbeat->StartDamage, afterbeat->StartRadius, afterbeat->StartKnockbackForce);
            }
            else
            {
                Fire(f, owner, afterbeat, afterbeat->EndPosition, afterbeat->EndDamage, afterbeat->EndRadius, afterbeat->EndKnockbackForce);
            }
        }

        // Inline overlap+damage+knockback loop (mirrors VortexSystem.TryRepulseOnDestroy's shape)
        // rather than HitEffectUtility.ApplyDamageInRadius/ApplyShockwave, since rank 3's own
        // Resonance-per-enemy-hit bonus needs the exact hit COUNT from this same sweep.
        private static void Fire(Frame f, EntityRef owner, ZaraAfterbeat* afterbeat, FPVector3 position, FP damage, FP radius, FP knockbackForce)
        {
            if (damage <= FP._0 && knockbackForce <= FP._0)
                return;

            if (radius <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            int enemiesHit = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (target == owner || f.Has<Enemy>(target) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                // generatesResonance: false - Afterbeat's own damage must not generate Resonance
                // through the generic per-damage hook; rank 3's own capped per-enemy-hit bonus below
                // is the only Resonance Afterbeat ever grants (confirmed with the user).
                if (damage > FP._0)
                {
                    DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Skill, generatesResonance: false);
                }

                if (knockbackForce > FP._0)
                {
                    DamageUtility.ApplyKnockback(f, target, targetTransform->Position - position, knockbackForce, FP._0, owner);
                }

                enemiesHit++;
            }

            // Own dedicated event (ZaraAfterbeatFxView) rather than ResonancePulseReleased/
            // ShockwaveReleased - see AfterbeatPulseReleased's own comment in Events.qtn for why.
            f.Events.AfterbeatPulseReleased(owner, position, radius);

            // Afterbeat rank 3 "Double Beat" - enemies hit generate additional Resonance, drawing on
            // the SAME per-dash allowance rank 1's dash sweep uses (see GrantCappedResonance), so the
            // two sources can never compound past MaxResonancePerDash between them.
            for (int i = 0; i < enemiesHit; i++)
            {
                GrantCappedResonance(f, owner, afterbeat);
            }
        }

        // The single per-dash-capped Resonance faucet, shared by rank 1's dash sweep
        // (AfterbeatSkillAction.SweepForResonance) and rank 3's pulse hits above - one allowance, one
        // place that spends it, so neither can quietly bypass the other's cap. Public for the skill
        // action's own sweep to call in.
        public static void GrantCappedResonance(Frame f, EntityRef owner, ZaraAfterbeat* afterbeat)
        {
            if (afterbeat->ResonancePerEnemyHit <= FP._0)
                return;

            FP remainingCap = afterbeat->MaxResonancePerDash - afterbeat->ResonanceGrantedThisDash;

            if (remainingCap <= FP._0)
                return;

            FP grant = FPMath.Min(afterbeat->ResonancePerEnemyHit, remainingCap);
            ResonanceUtility.Grant(f, owner, grant);
            afterbeat->ResonanceGrantedThisDash += grant;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ZaraAfterbeat* Afterbeat;
        }
    }
}
