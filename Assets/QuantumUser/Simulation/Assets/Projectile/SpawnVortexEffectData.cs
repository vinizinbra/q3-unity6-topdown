namespace Quantum
{
    using Photon.Deterministic;

    // SpawnEntityEffectData that also configures the spawned entity's Vortex (see VortexSystem) -
    // Force comes from context.Damage (the triggering hit's own damage, already boosted by
    // IncreaseProjectileDamageSkillAction if equipped) by default, unless overridden by
    // VortexPowerPulseUpgrade. Radius isn't configured here either - it comes from the spawned
    // entity's own PhysicsCollider3D (a Sphere), which SpawnedEntitySpawner's existing
    // SpawnRadiusUpgrade already knows how to scale generically.
    public unsafe class SpawnVortexEffectData : SpawnEntityEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, context.Owner, Prototype, Duration, context.Position, context.Source, context.Element);

            if (f.Unsafe.TryGetPointer<Vortex>(spawned, out var vortex) == false)
            {
                Log.Error($"[Skill] {spawned} has no Vortex component - is the Prototype actually the vortex, not a plain SpawnEntityEffectData target?");
                return;
            }

            vortex->Force = context.Damage;

            ApplyPowerUpgrade(f, context.Owner, vortex);
            ApplyDamageUpgrade(f, context.Owner, spawned);
            ApplyExplodeOnDestroyUpgrade(f, context.Owner, spawned);
            ApplyRandomExplosionUpgrade(f, context.Owner, spawned);
            ApplyMarkUpgrade(f, context.Owner, spawned);
            ApplyCrowdDamageUpgrade(f, context.Owner, spawned);
            ApplyHomingProjectileUpgrade(f, context.Owner, spawned);

            Log.Debug($"[Skill] {spawned} spawned as a Vortex with Force {vortex->Force} (from context.Damage unless overridden)");
        }

        // Begin-only upgrade (see VortexPowerPulseUpgrade) baked in here rather than checked live -
        // same race-avoidance reasoning as every other spawn-configuring upgrade
        // (SpawnAlternatingAreaEffectData's ApplyXUpgrade methods): this runs while the throw is
        // still guaranteed Active, before the granting skill's own End could possibly beat it here.
        // An absolute override for both fields, not a bonus - replaces "Force = context.Damage" and
        // whatever TickInterval is authored on the prototype outright.
        private static void ApplyPowerUpgrade(Frame f, EntityRef owner, Vortex* vortex)
        {
            if (f.Unsafe.TryGetPointer<VortexPowerPulseUpgrade>(owner, out var upgrade) == false)
                return;

            vortex->Force = upgrade->Power;
            vortex->TickInterval = upgrade->TickInterval;
            vortex->TickTimer = FP._0;
        }

        // Begin-only upgrade (see VortexDamageUpgrade) - attaches a real AreaDamage component onto
        // the spawned vortex rather than a bespoke damage-tick system, so it re-pulses on
        // AreaDamageSystem's own schedule (independent of the pull's TickInterval above) using the
        // exact same collider the pull already reads its radius from. Enemies only - a vortex
        // shouldn't damage the caster's own allies standing in its pull radius.
        private static void ApplyDamageUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexDamageUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<AreaDamage>(spawned, out var area);
            area->Damage = upgrade->Damage;
            area->TickInterval = upgrade->TickInterval;
            area->TickTimer = FP._0;
            area->TargetMask = DamageTargetMask.Enemies;
            area->Effects[0] = upgrade->DamageEffect;
        }

        // Begin-only upgrade (see VortexExplodeOnDestroy) - copied onto the spawned vortex itself
        // (not re-read off the owner later) so VortexSystem.TryExplodeOnDestroy has everything it
        // needs even if the owner is long gone by the time a long-Duration vortex finally expires.
        private static void ApplyExplodeOnDestroyUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexExplodeOnDestroy>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<VortexExplodeOnDestroy>(spawned, out var copy);
            copy->Damage = upgrade->Damage;
            copy->Source = upgrade->Source;
        }

        // Begin-only upgrade (see VortexRandomExplosionUpgrade) - copied onto the spawned vortex
        // itself, same reasoning as ApplyExplodeOnDestroyUpgrade. TickTimer starts at 0 so the first
        // mini-explosion doesn't wait out a full TickInterval before ever firing.
        private static void ApplyRandomExplosionUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexRandomExplosionUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<VortexRandomExplosionUpgrade>(spawned, out var copy);
            copy->Damage = upgrade->Damage;
            copy->Radius = upgrade->Radius;
            copy->TickInterval = upgrade->TickInterval;
            copy->TickTimer = FP._0;
            copy->Source = upgrade->Source;
        }

        // Begin-only upgrade (see VortexMarkUpgrade) - copied onto the spawned vortex itself, same
        // reasoning as the other Apply*Upgrade methods here.
        private static void ApplyMarkUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexMarkUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<VortexMarkUpgrade>(spawned, out var copy);
            copy->MarkEffect = upgrade->MarkEffect;
        }

        // Begin-only upgrade (see VortexCrowdDamageUpgrade) - copied onto the spawned vortex itself,
        // same reasoning as the other Apply*Upgrade methods here.
        private static void ApplyCrowdDamageUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexCrowdDamageUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<VortexCrowdDamageUpgrade>(spawned, out var copy);
            copy->PerEnemyBonus = upgrade->PerEnemyBonus;
            copy->MaxCount = upgrade->MaxCount;
        }

        // Begin-only upgrade (see VortexHomingProjectileUpgrade) - copied onto the spawned vortex
        // itself, same reasoning as ApplyExplodeOnDestroyUpgrade. TickTimer starts at 0 so the first
        // homing shot doesn't wait out a full TickInterval before ever firing.
        private static void ApplyHomingProjectileUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexHomingProjectileUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<VortexHomingProjectileUpgrade>(spawned, out var copy);
            copy->Projectile = upgrade->Projectile;
            copy->Damage = upgrade->Damage;
            copy->SearchRadiusMultiplier = upgrade->SearchRadiusMultiplier;
            copy->TickInterval = upgrade->TickInterval;
            copy->TickTimer = FP._0;
        }
    }
}
