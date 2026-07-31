namespace Quantum
{
    using Photon.Deterministic;

    // Grants (or refills) shield on the target - added to the shared Hit Effect list (not under
    // Assets/Enemy/) since weapons/skills could use this too, not just an enemy support type. No
    // existing effect grants Shield today (ShieldSystem only recharges what's already there); this
    // is the first HitEffectData that does. Built for the Shielder roster entry, which targets its
    // highest-max-health ally (HighestHealthAllyInRangeTargetingData) and applies this to them.
    public unsafe class ShieldEffectData : HitEffectData
    {
        public FP Amount = 20;

        // True: Amount also raises Shield.Max (a capacity buff); Current is topped up to match.
        // False: Amount only refills Current, capped at whatever Max already is.
        public bool IncreaseMax = true;

        // Only applied the first time this grants a Shield the target didn't already have - an
        // existing shield keeps whatever recharge tuning it was originally granted with; this
        // effect only tops it up/raises its cap from then on.
        public FP RechargeDelay = 3;
        public FP RechargeRate = 10;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            bool alreadyHadShield = f.Has<Shield>(context.Target);
            f.AddOrGet<Shield>(context.Target, out var shield);

            if (alreadyHadShield == false)
            {
                shield->RechargeDelay = RechargeDelay;
                shield->RechargeRate = RechargeRate;
            }

            // A brand-new Shield starts with Max 0, so even a IncreaseMax=false grant needs to
            // establish a capacity - otherwise Current below is capped at 0 and nothing is granted.
            if (IncreaseMax == true || alreadyHadShield == false)
            {
                shield->Max += Amount;
            }

            ShieldUtility.ApplyFlatShield(f, context.Target, context.Owner, shield, Amount);
        }
    }
}
