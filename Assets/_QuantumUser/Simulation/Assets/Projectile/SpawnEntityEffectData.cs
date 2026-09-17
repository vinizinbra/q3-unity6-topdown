namespace Quantum
{
    using Photon.Deterministic;

    // Drops a prefab where the hit landed - a grenade that keeps burning after the blast. What the
    // spawned thing then does is the prototype's own business (an AreaDamage on it hurts whoever
    // stands in it), so this is the projectile-side twin of SpawnEntitySkillAction: the same spawn
    // off a different trigger.
    public unsafe class SpawnEntityEffectData : HitEffectData
    {
        public AssetRef<EntityPrototype> Prototype;

        public FP Duration = 3;

        // 0 (the default) leaves the spawned prototype's own authored AreaDamage.Damage alone - the
        // flat, per-tick value it's always had. Above 0, this is a per-TICK multiplier of the live
        // weapon Damage that hit-triggered this spawn (context.Damage), NOT a total-over-lifetime
        // multiplier - total DoT dealt is this times however many ticks Duration/AreaDamage.TickInterval
        // works out to (see AreaDamage.qtn's own comment on that math). Lets a weapon-spawned area
        // (a grenade's lingering fire) scale with the weapon's own damage growth, while a shared
        // prototype also spawned from elsewhere with no live damage to scale off (e.g. Max's Ignition
        // spawning this same prototype straight through SpawnedEntitySpawner, not through this effect)
        // keeps its own independently-authored flat number untouched.
        public FP DamageMultiplier = FP._0;

        // A lingering hazard exists once per blast, not once per target caught in it - see this
        // field's own comment on HitEffectData.
        public override bool AppliesOncePerBlast => true;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            FP? damageOverride = DamageMultiplier > FP._0 ? context.Damage * DamageMultiplier : (FP?)null;

            // AreaRadius > 0 is HitEffectContext's own "this hit has a meaningful area" signal (see
            // its own comment) - a multi-target blast spawns at the blast's own center, never at
            // whichever target's position this context happened to carry (see
            // HitEffectUtility.ApplyBlastLevelEffects, the only multi-target caller that ever reaches
            // this). A guaranteed single-connect hit (AreaRadius still 0) keeps spawning at Position,
            // exactly as before - that's already the real impact point for that path.
            FPVector3 position = context.AreaRadius > FP._0 ? context.AreaCenter : context.Position;

            SpawnedEntitySpawner.Spawn(f, context.Owner, Prototype, Duration, position, context.Source, context.Element, damageOverride);
        }
    }
}
