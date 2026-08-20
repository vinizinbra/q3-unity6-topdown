namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drains a sentry's own Health at a fixed rate over its lifetime (Sentry.DecayRate, computed once
    // at spawn - see SpawnSentrySkillAction) instead of a plain DestroyAfterTime countdown, so "ran
    // out of time" and "killed by damage" become the exact same death path (DamageUtility.ApplyDamage)
    // - any on-death upgrade (Overload Core) fires either way, the existing health bar doubles as a
    // visible time-remaining readout, and REMAINING LIFETIME is derivable rather than a second timer.
    // Routed through the normal damage pipeline rather than a direct Health write, so a Shield absorbs
    // it exactly like any other hit.
    //
    // Also owns the two lifetime-derived pieces of state that have nowhere better to live: Overclock
    // rank 3's Redline latch, and the countdown on any timed sentry-wide fire-rate buff.
    [Preserve]
    public unsafe class SentryDecaySystem : SystemMainThreadFilter<SentryDecaySystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            TickTempFireRate(f, filter.Sentry);
            TryLatchRedline(f, filter.Entity, filter.Sentry);

            if (filter.Sentry->DecayRate <= FP._0)
                return;

            FP damage = filter.Sentry->DecayRate * f.DeltaTime;
            DamageUtility.ApplyDamage(f, filter.Entity, damage, filter.Sentry->Owner, DamageSource.None,
                bypassOutgoingResolution: true, silent: true);
        }

        private static void TickTempFireRate(Frame f, Sentry* sentry)
        {
            if (sentry->TempFireRateRemaining <= FP._0)
                return;

            sentry->TempFireRateRemaining -= f.DeltaTime;
        }

        // Overclock rank 3 "Redline" - latches ON the first time REMAINING lifetime crosses the
        // threshold and stays on until this sentry dies. Deliberately the simple, one-way behavior the
        // brief prefers: extending lifetime afterwards (Emergency Repair, Relocation Protocol) does
        // NOT switch it back off, so those combinations read as a synergy rather than a trap, and
        // there's no oscillation to reason about.
        private static void TryLatchRedline(Frame f, EntityRef entity, Sentry* sentry)
        {
            if (sentry->RedlineActive == true || sentry->RedlineThreshold <= FP._0)
                return;

            if (SentryUtility.GetRemainingLifetime(f, entity, sentry) > sentry->RedlineThreshold)
                return;

            sentry->RedlineActive = true;
            f.Events.SentryRedlineEngaged(entity);

            Log.Debug($"[Sentry] {entity} entered Redline (final {sentry->RedlineThreshold}s of lifetime)");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Sentry* Sentry;
        }
    }
}
