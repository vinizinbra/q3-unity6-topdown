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
        // rather than HitEffectUtility.ApplyDamageInRadius/ApplyShockwave, since rank 3 needs to know
        // whether this sweep caught ANYTHING at all.
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

                if (damage > FP._0)
                {
                    DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Skill);
                }

                if (knockbackForce > FP._0)
                {
                    DamageUtility.ApplyKnockback(f, target, targetTransform->Position - position, knockbackForce, FP._0, owner);
                }

                enemiesHit++;
            }

            // Own dedicated event (ZaraAfterbeatFxView) rather than the shared ShockwaveReleased - see
            // AfterbeatPulseReleased's own comment in Events.qtn for why.
            f.Events.AfterbeatPulseReleased(owner, position, radius);

            // Afterbeat rank 3 "Double Beat" - a chunk of Flow if this pulse caught anything, and at
            // most once per dash across BOTH pulses (FlowGrantedThisDash, reset on dash Begin). Flat,
            // never per-enemy: the reward is for landing the beat at all, so a dash into a crowd is
            // worth exactly what a dash into a single target is.
            if (enemiesHit > 0)
            {
                TryGrantPulseFlow(f, owner, afterbeat);
            }
        }

        // The single per-dash Flow grant for rank 3. One allowance, one place that spends it, so the
        // Start and End pulses can never each pay out.
        //
        // Sized as a fraction of the bar rather than read off the skill asset: this system ticks a
        // countdown parked on Zara long after the action that scheduled it has finished, so it has no
        // asset in hand - the same reason every other value it needs was baked onto ZaraAfterbeat at
        // dash time.
        private static readonly FP PulseFlowProgress = FP.FromString("0.35");

        private static void TryGrantPulseFlow(Frame f, EntityRef owner, ZaraAfterbeat* afterbeat)
        {
            if (afterbeat->GrantsFlowOnPulseHit == false || afterbeat->FlowGrantedThisDash == true)
                return;

            if (f.Unsafe.TryGetPointer<ZaraFlow>(owner, out var flow) == false)
                return;

            afterbeat->FlowGrantedThisDash = true;
            ZaraFlowUtility.AddProgress(f, owner, flow, PulseFlowProgress);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ZaraAfterbeat* Afterbeat;
        }
    }
}
