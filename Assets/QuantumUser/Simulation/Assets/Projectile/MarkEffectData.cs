namespace Quantum
{
    using Photon.Deterministic;

    // Multiplies incoming damage on the target for Duration - see
    // StatusEffectUtility.GetIncomingDamageMultiplier, applied once inside DamageUtility.ApplyDamage
    // so every damage source respects it identically. Not an ElementType, so unlike Burn/Ice/Poison
    // this is only ever explicitly authored onto an attack's Effects list, never part of the
    // elemental proc roll.
    public unsafe class MarkEffectData : HitEffectData
    {
        public FP Duration = 5;
        public FP DamageTakenMultiplier = FP.FromString("1.2");

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, Duration);

            StatusEffectUtility.ApplyMark(f, context.Target, duration, DamageTakenMultiplier);
        }
    }
}
