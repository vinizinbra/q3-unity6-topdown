namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // SpawnEntityEffectData that also configures the spawned entity's Vortex (see VortexSystem) and
    // deals Kai's flat Cast Damage directly to whatever the throw actually hit - see Apply below.
    // PullForce is its own baseline, decoupled from the throw's own Damage (which is now "Vortex Skill
    // Damage," the percentage basis every Ascension line scales off - see
    // KaiAscensionUtility.ResolveVortexSkillDamage - not the pull's own strength). Radius isn't
    // configured here either - it comes from the spawned entity's own PhysicsCollider3D (a Sphere),
    // which SpawnedEntitySpawner's existing SpawnRadiusUpgrade already knows how to scale generically.
    public unsafe class SpawnVortexEffectData : SpawnEntityEffectData
    {
        // Baseline pull strength, independent of Kai's own Cast Damage - see Singularity, which
        // multiplies this rather than overriding it. Placeholder pending a real balance pass.
        public FP PullForce = 8;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Cast Damage - dealt directly to whatever the throw actually hit (None if it struck level
            // geometry instead), separate from the vortex's own ongoing pull below. Uses the normal
            // DamageUtility.ApplyDamage entry point so it still flows through the standard
            // outgoing-damage pipeline (crit, global multipliers) rather than a bespoke reimplementation.
            if (context.Target != EntityRef.None)
            {
                DamageUtility.ApplyDamage(f, context.Target, context.Damage, context.Owner, context.Source);
            }

            EntityRef spawned = SpawnedEntitySpawner.Spawn(f, context.Owner, Prototype, Duration, context.Position, context.Source, context.Element);

            if (f.Unsafe.TryGetPointer<Vortex>(spawned, out var vortex) == false)
            {
                Log.Error($"[Skill] {spawned} has no Vortex component - is the Prototype actually the vortex, not a plain SpawnEntityEffectData target?");
                return;
            }

            vortex->Force = PullForce;

            // Skill Area (CharacterStats.AreaRadiusMultiplier) - grow the whole vortex once at spawn
            // by scaling its collider Sphere, the single radius VortexSystem reads for the pull, the
            // AreaDamage pulse, explode-on-destroy and homing search alike. Baked here (not re-read
            // per pulse), same as SpawnRadiusUpgrade's own collider scale in SpawnedEntitySpawner.
            // Singularity's own PullRadiusMultiplier (below) composes on top of this, not instead of it.
            FP areaMultiplier = StatUtility.GetAreaMultiplier(f, context.Owner);

            if (areaMultiplier != FP._1
                && f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == true
                && collider->Shape.Type == Shape3DType.Sphere)
            {
                collider->Shape.Sphere.Radius *= areaMultiplier;
            }

            ApplySingularityUpgrade(f, context.Owner, spawned, vortex);
            ApplyDamageUpgrade(f, context.Owner, spawned);
            ApplyImplosionUpgrade(f, context.Owner, spawned);
            ApplyExplodeOnDestroyUpgrade(f, context.Owner, spawned);
            ApplyCrowdDamageUpgrade(f, context.Owner, spawned);
            ApplyHomingProjectileUpgrade(f, context.Owner, spawned);

            Log.Debug($"[Skill] {spawned} spawned as a Vortex with Force {vortex->Force}");
        }

        // Begin-only upgrade (see SingularityUpgrade) baked in here rather than checked live - same
        // race-avoidance reasoning as every other spawn-configuring upgrade
        // (SpawnAlternatingAreaEffectData's ApplyXUpgrade methods): this runs while the throw is
        // still guaranteed Active, before the granting skill's own End could possibly beat it here.
        // MULTIPLIES Force/collider radius rather than overriding them (the old VortexPowerPulseUpgrade
        // this replaces was an absolute override) - composes with Skill Area/PullForce baseline instead
        // of replacing them outright.
        private static void ApplySingularityUpgrade(Frame f, EntityRef owner, EntityRef spawned, Vortex* vortex)
        {
            if (f.Unsafe.TryGetPointer<SingularityUpgrade>(owner, out var upgrade) == false)
                return;

            vortex->Force *= upgrade->PullForceMultiplier;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(spawned, out var collider) == true
                && collider->Shape.Type == Shape3DType.Sphere)
            {
                collider->Shape.Sphere.Radius *= upgrade->PullRadiusMultiplier;
            }

            f.AddOrGet<VortexInterruptConfig>(spawned, out var interruptConfig);
            interruptConfig->MaxEligibleTierIndex = upgrade->MaxEligibleTierIndex;
            interruptConfig->UnlimitedBelowOrEqualTierIndex = upgrade->UnlimitedBelowOrEqualTierIndex;

            if (upgrade->HasGravityPulse == true)
            {
                f.AddOrGet<VortexGravityPulse>(spawned, out var gravityPulse);
                gravityPulse->Force = vortex->Force * upgrade->GravityPulseForceMultiplier;
                gravityPulse->Interval = upgrade->GravityPulseInterval;
                gravityPulse->Timer = FP._0;
            }
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

        // Compression rank 3 only (see VortexImplosionUpgrade) - copied onto the spawned vortex
        // itself, same reasoning as every other Apply*Upgrade method here.
        private static void ApplyImplosionUpgrade(Frame f, EntityRef owner, EntityRef spawned)
        {
            if (f.Unsafe.TryGetPointer<VortexImplosionUpgrade>(owner, out var upgrade) == false)
                return;

            f.AddOrGet<VortexImplosionUpgrade>(spawned, out var copy);
            copy->DamagePercent = upgrade->DamagePercent;
            copy->RadiusFraction = upgrade->RadiusFraction;
            copy->EveryNthPulse = upgrade->EveryNthPulse;
            copy->PulseCounter = 0;
            copy->Source = upgrade->Source;
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
            copy->RadiusMultiplier = upgrade->RadiusMultiplier;
            copy->PreExplosionPullForce = upgrade->PreExplosionPullForce;
            copy->Source = upgrade->Source;
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
            copy->ShardCount = upgrade->ShardCount;
            copy->PierceCount = upgrade->PierceCount;
        }
    }
}
