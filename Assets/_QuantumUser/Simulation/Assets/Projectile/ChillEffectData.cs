namespace Quantum
{
    using Photon.Deterministic;

    // Ice/Chill in a FIXED amount per application, not scaled by damage like SlowEffectData - for a channelled hazard
    // that must chill on a predictable schedule whatever its tick damage is (the Frost Patriarch's Polar Breath: the
    // first ticks only slow, staying in it long enough reaches Freeze). Feeds the one existing meter through
    // StatusEffectUtility.ApplyIce (slow grows with buildup, Freeze at the threshold, nothing builds while Frozen) - no
    // second cold gauge.
    //
    // PlayerFreezeRecovery: ApplyFreeze only grants its anti-permafreeze window (FreezeRecoveryRemaining) to targets with
    // an enemy tier resistance - heroes have none. When THIS application is the one that freezes a hero, the same window
    // is granted here, so the next Freeze from any source can't land until it runs out. 0 opts out.
    public unsafe class ChillEffectData : HitEffectData
    {
        public FP BuildupPerHit = FP._1;
        // How long the chill lingers after the last application before the buildup resets (StatusEffects.IceRemaining).
        public FP Duration = FP._1_50;
        public FP PlayerFreezeRecovery = 3;
        // >0: this effect never pushes the target's buildup past this (it still refreshes the chill) - a slow that reads
        // with the usual Chill feedback but can never Freeze on its own (the Frost Patriarch's leap wave). 0 = no cap.
        public FP MaxBuildup = FP._0;

        public override void Apply(Frame f, ref HitEffectContext context) => Apply(f, ref context, FP._1, FP._1);

        public override void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
        {
            if (context.Target == EntityRef.None || f.Unsafe.TryGetPointer<StatusEffects>(context.Target, out var status) == false)
                return;

            bool wasFrozen = status->FreezeRemaining > FP._0;
            FP buildup = BuildupPerHit * magnitudeMultiplier;
            if (MaxBuildup > FP._0)
                buildup = FPMath.Max(FP._0, FPMath.Min(buildup, MaxBuildup - status->IceBuildup));

            StatusEffectUtility.ApplyIce(f, context.Target, Duration * durationMultiplier, buildup);

            if (wasFrozen == false && status->FreezeRemaining > FP._0 && PlayerFreezeRecovery > FP._0 &&
                f.Has<PlayerLink>(context.Target) == true && status->FreezeRecoveryRemaining <= FP._0)
            {
                status->FreezeRecoveryRemaining = status->FreezeRemaining + PlayerFreezeRecovery;
            }

            StatusEffectUtility.TryTriggerElementalReaction(f, context.Target, context.Owner, context.Source, ElementType.Ice, context.Damage);
        }
    }
}
