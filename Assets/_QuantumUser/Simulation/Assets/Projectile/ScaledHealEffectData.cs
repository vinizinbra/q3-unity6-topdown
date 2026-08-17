namespace Quantum
{
    using Photon.Deterministic;

    // Heal-side mirror of DamageEffectData - scales whatever percent-of-MaxHealth AlternatingArea
    // already resolved into HitEffectContext.Damage (see AlternatingAreaSystem's heal-phase branch/
    // SpawnAlternatingAreaEffectData.ResolveHealAmount), rather than carrying its own fixed percent
    // the way the older HealEffectData does. Lets Zara's Totem/Portable Speaker Healing Beats share
    // one baked-per-cast percent (ranked by Healing Chorus) across a whole HealEffects list, the same
    // "one FP on the component, effects just scale it" shape the Damage side already had.
    public unsafe class ScaledHealEffectData : HitEffectData
    {
        public FP HealMultiplier = FP._1;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            HealUtility.ApplyHeal(f, context.Target, context.Owner, context.Damage * HealMultiplier);
        }
    }
}
