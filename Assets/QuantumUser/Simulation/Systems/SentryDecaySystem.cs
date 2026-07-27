namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drains a sentry's own Health at a fixed rate over its lifetime (Sentry.DecayRate, computed once
    // at spawn - see SpawnSentrySkillAction) instead of a plain DestroyAfterTime countdown, so "ran
    // out of time" and "killed by damage" become the exact same death path (DamageUtility.ApplyDamage)
    // - any on-death upgrade (e.g. SentryAddOverloadSkillAction) fires either way, and the existing
    // health bar doubles as a visible time-remaining readout. Routed through the normal damage
    // pipeline rather than a direct Health write, so an equipped Shield absorbs/soaks it exactly like
    // any other hit - a sentry with no Shield (the base skill) just drains straight through as
    // intended; Add Shield turns that decay into a real "survives longer" upgrade.
    [Preserve]
    public unsafe class SentryDecaySystem : SystemMainThreadFilter<SentryDecaySystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Sentry->DecayRate <= FP._0)
                return;

            FP damage = filter.Sentry->DecayRate * f.DeltaTime;
            DamageUtility.ApplyDamage(f, filter.Entity, damage, filter.Sentry->Owner, DamageSource.None,
                bypassOutgoingResolution: true, silent: true);
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Sentry* Sentry;
        }
    }
}
