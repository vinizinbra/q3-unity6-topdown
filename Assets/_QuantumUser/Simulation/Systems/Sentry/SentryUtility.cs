namespace Quantum
{
    using Photon.Deterministic;

    // Shared helpers for Lux's Sentry - the small set of operations several Ascensions each need and
    // which would otherwise be re-derived (badly) in three places: how long a sentry has left, how to
    // give it more, how to find the ones a given Lux owns, and how to enforce her active-Sentry cap.
    //
    // Lifetime is deliberately NOT a second timer. A sentry's Health drains at Sentry.DecayRate (see
    // Sentry.qtn), so remaining lifetime is exactly CurrentHealth / DecayRate and extending it is
    // exactly adding DecayRate * seconds of Health. One source of truth, and the existing health bar
    // keeps doubling as a time-remaining readout for free.
    public static unsafe class SentryUtility
    {
        // Seconds of life left at the current decay rate. FP.MaxValue for a sentry that isn't decaying
        // at all (DecayRate 0 - never happens in practice, but "infinite" is the honest answer and
        // stops a caller dividing by zero).
        public static FP GetRemainingLifetime(Frame f, EntityRef sentry, Sentry* data)
        {
            if (data->DecayRate <= FP._0)
                return FP.MaxValue;

            if (f.Unsafe.TryGetPointer<Health>(sentry, out var health) == false)
                return FP._0;

            return health->CurrentHealth / data->DecayRate;
        }

        // Grants `seconds` of extra life, drawn from this sentry's own remaining extension budget
        // (Sentry.LifetimeExtensionRemaining, seeded at deploy time). Returns how much was actually
        // granted. Capping per SENTRY rather than per Lux is what stops a dash-cooldown build from
        // keeping one machine alive indefinitely while still letting her extend each new one.
        public static FP TryExtendLifetime(Frame f, EntityRef sentry, Sentry* data, FP seconds)
        {
            if (seconds <= FP._0 || data->DecayRate <= FP._0)
                return FP._0;

            FP granted = FPMath.Min(seconds, data->LifetimeExtensionRemaining);

            if (granted <= FP._0)
                return FP._0;

            if (f.Unsafe.TryGetPointer<Health>(sentry, out var health) == false)
                return FP._0;

            // MaxHealth rises alongside CurrentHealth, so the health bar keeps reading as a true
            // time-remaining gauge instead of silently pinning at full after an extension.
            FP extraHealth = data->DecayRate * granted;
            health->MaxHealth += extraHealth;
            health->CurrentHealth += extraHealth;

            data->LifetimeExtensionRemaining -= granted;
            return granted;
        }

        // Repairs a fraction of the sentry's own MaxHealth - which, given the decay model, is also
        // literally "give it that fraction of its total lifetime back". Clamped at MaxHealth.
        public static void Repair(Frame f, EntityRef sentry, FP fractionOfMax)
        {
            if (fractionOfMax <= FP._0 || f.Unsafe.TryGetPointer<Health>(sentry, out var health) == false)
                return;

            health->CurrentHealth = FPMath.Min(health->MaxHealth, health->CurrentHealth + health->MaxHealth * fractionOfMax);
        }

        // Applies a timed sentry-wide fire-rate multiplier (Emergency Overclock, Rapid Setup).
        // Take-the-stronger/longer on reapply, same semantics every other timed multiplier in this
        // codebase uses - a weaker proc landing during a stronger one can't cut it short.
        public static void ApplyTempFireRate(Sentry* data, FP multiplier, FP duration)
        {
            if (multiplier <= FP._1 || duration <= FP._0)
                return;

            bool active = data->TempFireRateRemaining > FP._0;

            if (active == false || multiplier >= data->TempFireRateMultiplier)
            {
                data->TempFireRateMultiplier = multiplier;
                data->TempFireRateRemaining = duration;
            }
            else if (duration > data->TempFireRateRemaining)
            {
                data->TempFireRateRemaining = duration;
            }
        }

        // The single resolution point for "how fast does this sentry shoot right now" - permanent
        // (Overclock/Field Modifications), timed (Emergency Overclock/Rapid Setup) and Redline all
        // compose multiplicatively here rather than each writing barrels directly. Read every tick by
        // SentryBarrelSystem, so any change takes effect immediately on already-attached barrels and
        // can never compound across ticks.
        public static FP ResolveFireRateMultiplier(Sentry* data)
        {
            FP multiplier = data->FireRateMultiplier > FP._0 ? data->FireRateMultiplier : FP._1;

            if (data->TempFireRateRemaining > FP._0 && data->TempFireRateMultiplier > FP._0)
            {
                multiplier *= data->TempFireRateMultiplier;
            }

            if (data->RedlineActive == true && data->RedlineFireRateMultiplier > FP._0)
            {
                multiplier *= data->RedlineFireRateMultiplier;
            }

            return multiplier;
        }

        // The closest sentry this owner has within maxDistance, or None. Used by Emergency Repair
        // (dash ends near one) and Relocation Protocol (dash while near one) - both explicitly scoped
        // to sentries this Lux owns, so a teammate Lux's machine is never affected.
        public static EntityRef FindOwnedSentryNear(Frame f, EntityRef owner, FPVector3 position, FP maxDistance)
        {
            EntityRef best = EntityRef.None;
            FP bestSqrDistance = maxDistance * maxDistance;

            var sentries = f.Filter<Sentry, Transform3D>();

            while (sentries.Next(out EntityRef entity, out Sentry sentry, out Transform3D transform))
            {
                if (sentry.Owner != owner)
                    continue;

                FP sqrDistance = (transform.Position - position).SqrMagnitude;

                if (sqrDistance > bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                best = entity;
            }

            return best;
        }

        // The most recently deployed sentry this owner has, resolved as "the one with the most
        // lifetime left" - Field Modifications' documented default target rule ("apply the upgrade to
        // the most recently deployed active Sentry", rather than buffing every sentry at once).
        // Deterministic and needs no spawn-order bookkeeping.
        public static EntityRef FindNewestOwnedSentry(Frame f, EntityRef owner)
        {
            EntityRef best = EntityRef.None;
            FP bestRemaining = FP.MinValue;

            var sentries = f.Filter<Sentry, Health>();

            while (sentries.Next(out EntityRef entity, out Sentry sentry, out Health health))
            {
                if (sentry.Owner != owner || sentry.DecayRate <= FP._0)
                    continue;

                FP remaining = health.CurrentHealth / sentry.DecayRate;

                if (remaining <= bestRemaining)
                    continue;

                bestRemaining = remaining;
                best = entity;
            }

            return best;
        }
    }
}
