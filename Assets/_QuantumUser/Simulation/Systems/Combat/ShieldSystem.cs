namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Recharges shields back to full after their owner has gone untouched for RechargeDelay - the
    // delay itself is reset by DamageUtility on every hit that lands, not here.
    [Preserve]
    public unsafe class ShieldSystem : SystemMainThreadFilter<ShieldSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            Shield* shield = filter.Shield;

            if (shield->Current >= shield->Max)
                return;

            bool hasShieldRegenBuff = StatusEffectUtility.HasShieldRegenBuff(f, filter.Entity);

            if (shield->RechargeTimer > FP._0 && hasShieldRegenBuff == false)
            {
                shield->RechargeTimer -= f.DeltaTime;

                if (shield->RechargeTimer <= FP._0)
                    Log.Debug($"[Shield] {filter.Entity} delay elapsed - recharging {shield->Current}/{shield->Max} at {shield->RechargeRate}/s");

                return;
            }

            // ShieldRegen buffed (see StatusEffectUtility.HasShieldRegenBuff) - skips the wait above
            // entirely rather than ticking RechargeTimer down early, so it resumes counting down from
            // wherever it was once the buff itself ends instead of the buff "banking" free delay time.

            // A shield below Max with nothing to recharge by is stuck for good, so say so rather
            // than adding zero every tick in silence. Throttled to once a second - the tick rate
            // would otherwise flood the log.
            if (shield->RechargeRate <= FP._0)
            {
                if (f.Number % f.UpdateRate == 0)
                    Log.Error($"[Shield] {filter.Entity} stuck at {shield->Current}/{shield->Max} - RechargeRate is 0, so it can never recharge (never seeded from CharacterData?)");

                return;
            }

            // ShieldRegen (see StatusEffectUtility) multiplies the authored rate rather than
            // replacing it - a sentry's Shield Area Rate aura makes an already-working recharge
            // faster, it doesn't unstick a RechargeRate of 0 (the check above already errors on that
            // regardless of any multiplier, since 0 times anything is still 0).
            FP rechargeRate = shield->RechargeRate * StatusEffectUtility.GetShieldRegenMultiplier(f, filter.Entity);

            shield->Current = FPMath.Min(shield->Max, shield->Current + rechargeRate * f.DeltaTime);

            if (shield->Current >= shield->Max)
                Log.Debug($"[Shield] {filter.Entity} back to full at {shield->Max}");
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Shield* Shield;
        }
    }
}
