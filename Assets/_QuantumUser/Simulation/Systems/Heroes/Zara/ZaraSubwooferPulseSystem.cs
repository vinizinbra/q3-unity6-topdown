namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Ticks down ZaraSubwooferPulse.Remaining (see ResonanceUtility.FirePulse, which schedules it -
    // Heavy Bass rank 3 "Subwoofer") and fires the delayed second shockwave once it hits 0 - same
    // "countdown component ticked by its own tiny System" shape ZaraAfterbeat/ZaraAfterbeatSystem
    // already use for Afterbeat's own delayed pulses. Damage + knockback in one inline overlap loop
    // (mirrors VortexSystem.TryRepulseOnDestroy's shape) rather than
    // HitEffectUtility.ApplyDamageInRadius/ApplyShockwave, since both a damage number AND a
    // knockback force are needed from the same sweep, and the damage call needs
    // generatesResonance: false. Enemies only, no healing - per spec's explicit default.
    [Preserve]
    public unsafe class ZaraSubwooferPulseSystem : SystemMainThreadFilter<ZaraSubwooferPulseSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            ZaraSubwooferPulse* sub = filter.Subwoofer;

            if (sub->Remaining <= FP._0)
                return;

            sub->Remaining -= f.DeltaTime;

            if (sub->Remaining > FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(sub->Radius);
            var hits = f.Physics3D.OverlapShape(sub->Position, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (target == filter.Entity || f.Has<Enemy>(target) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                // generatesResonance: false - Subwoofer's own damage must not generate more
                // Resonance, same exclusion as the main Resonance Pulse itself.
                DamageUtility.ApplyDamage(f, target, sub->Damage, filter.Entity, DamageSource.Skill, generatesResonance: false);

                if (sub->KnockbackForce > FP._0)
                {
                    DamageUtility.ApplyKnockback(f, target, targetTransform->Position - sub->Position, sub->KnockbackForce, FP._0, filter.Entity);
                }
            }

            f.Events.ShockwaveReleased(filter.Entity, sub->Position, sub->Radius, default);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public ZaraSubwooferPulse* Subwoofer;
        }
    }
}
