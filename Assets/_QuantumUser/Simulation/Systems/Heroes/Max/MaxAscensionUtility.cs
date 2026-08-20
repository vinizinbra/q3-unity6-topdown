namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared helpers for Max's Ascension lines - see docs/max-ascensions.md. Mirrors
    // BruteAscensionUtility's exact shape.
    public static unsafe class MaxAscensionUtility
    {
        // Full Throttle (Overdrive rank 2) - toggles CharacterStats.WeaponDamageMultiplier/
        // ReloadSpeedMultiplier in/out exactly at the max-Rage threshold, same enter/exit-threshold
        // toggle shape JuggernautSkillData.UpdateSpeedBoost already established for Brute's
        // Charged-speed tier. Called from RageOverdriveUtility.TryAdvanceStack (entering) and
        // ResetStacks/Revert (leaving) - never ticked, only ever toggled on the threshold crossing.
        public static void ApplyFullThrottle(Frame f, EntityRef owner, FullThrottleUpgrade* fullThrottle)
        {
            if (fullThrottle->Applied == true)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            stats->WeaponDamageMultiplier *= FP._1 + fullThrottle->WeaponDamageBonus;
            stats->ReloadSpeedMultiplier *= FP._1 + fullThrottle->ReloadSpeedBonus;
            fullThrottle->Applied = true;

            // Rank 3 - a single refill on the threshold crossing itself. Applied (just latched above)
            // is what guarantees "once per entry into max Rage", not a per-tick live condition.
            if (fullThrottle->HasInstantReload == true)
            {
                WeaponSystem.RefillMagazine(f, owner);
            }

            Log.Debug($"[Skill] {owner} Full Throttle engaged (+{fullThrottle->WeaponDamageBonus} Weapon Damage, +{fullThrottle->ReloadSpeedBonus} Reload Speed)");
        }

        public static void RevertFullThrottle(Frame f, EntityRef owner, FullThrottleUpgrade* fullThrottle)
        {
            if (fullThrottle->Applied == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == true)
            {
                stats->WeaponDamageMultiplier /= FP._1 + fullThrottle->WeaponDamageBonus;
                stats->ReloadSpeedMultiplier /= FP._1 + fullThrottle->ReloadSpeedBonus;
            }

            fullThrottle->Applied = false;
        }

        // Ignition - reacts to the SAME max-Rage threshold crossing Full Throttle does, so both are
        // driven from the one RageOverdriveUtility.TryAdvanceStack/ResetStacks pair rather than each
        // polling every tick. Rank 1's BurnOnHitStacks toggle reuses the existing, already-generic
        // TryApplyGuaranteedBurn weapon-hit hook - just conditional on max Rage now instead of the
        // old baseline's unconditional Begin/End toggle. Rank 3's Inferno pulse fires at most once
        // per Overdrive activation (InfernoTriggeredThisActivation, reset by IgnitionSkillAction on
        // Begin) the first time max Rage is reached.
        public static void OnEnteredMaxRage(Frame f, EntityRef owner, IgnitionUpgrade* ignition)
        {
            if (ignition->Applied == false && f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == true)
            {
                stats->BurnOnHitStacks += ignition->BurnOnHitStacks;
                ignition->Applied = true;
            }

            if (ignition->HasInferno == true && ignition->InfernoTriggeredThisActivation == false
                && f.Unsafe.TryGetPointer<Transform3D>(owner, out var transform) == true)
            {
                ignition->InfernoTriggeredThisActivation = true;
                // Skill Area - see StatUtility.GetAreaMultiplier. Max's kit is mostly self-buff, but
                // Ignition's Inferno burst and Burning Ground below are genuine skill areas and scale
                // like every other hero's.
                ApplyRadialBurn(f, transform->Position, ignition->InfernoRadius * StatUtility.GetAreaMultiplier(f, owner), owner, FP._0,
                    ignition->InfernoBurnDuration, ignition->InfernoBurnIntensity);
            }
        }

        public static void RevertIgnition(Frame f, EntityRef owner, IgnitionUpgrade* ignition)
        {
            if (ignition->Applied == false)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == true)
            {
                stats->BurnOnHitStacks -= ignition->BurnOnHitStacks;
            }

            ignition->Applied = false;
        }

        // Ignition rank 2 - drops a burning-ground patch at a Burning enemy's death position (see
        // MaxOverdriveReactionSystem.TryDropBurningGround). Every value comes off IgnitionUpgrade
        // rather than the prototype, so the patch can be balanced without opening a prefab; the
        // prototype only supplies the collider shape and whatever AreaDamage.Effects it authors (a
        // Burn effect, typically), which are left exactly as authored.
        public static void SpawnBurningGround(Frame f, EntityRef owner, IgnitionUpgrade* ignition, FPVector3 position)
        {
            if (ignition->BurningGroundPrototype.IsValid == false)
            {
                Log.Error($"[Skill] {owner}'s Ignition rank 2 has no BurningGroundPrototype assigned - nothing spawned");
                return;
            }

            EntityRef patch = SpawnedEntitySpawner.Spawn(f, owner, ignition->BurningGroundPrototype,
                ignition->BurningGroundDuration, position, DamageSource.Skill);

            if (patch == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<AreaDamage>(patch, out var area) == true)
            {
                area->Damage = ignition->BurningGroundDamage;

                if (ignition->BurningGroundTickInterval > FP._0)
                {
                    area->TickInterval = ignition->BurningGroundTickInterval;
                }
            }

            // Radius is authored here too, so one prototype can serve every rank/tuning pass - same
            // "spawn, then configure the spawned entity's own collider" shape
            // SpawnAlternatingAreaEffectData.ApplyMainStageRadius already uses.
            if (ignition->BurningGroundRadius > FP._0
                && f.Unsafe.TryGetPointer<PhysicsCollider3D>(patch, out var collider) == true
                && collider->Shape.Type == Shape3DType.Sphere)
            {
                collider->Shape.Sphere.Radius = ignition->BurningGroundRadius * StatUtility.GetAreaMultiplier(f, owner);
            }
        }

        // Damage + Burn to every enemy in radius - Ignition rank 3's Inferno pulse calls this instead
        // of re-deriving the same OverlapShape/Enemy-gate loop. damage may be 0 to apply Burn only
        // (Inferno's own pulse - the ignited Burn tick is the actual damage here, not a separate
        // upfront hit). Mirrors BruteAscensionUtility.ApplyRadialStunDamage's exact shape.
        public static void ApplyRadialBurn(Frame f, FPVector3 center, FP radius, EntityRef owner, FP damage, FP burnDuration, FP burnIntensity)
        {
            if (radius <= FP._0)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                if (damage > FP._0)
                {
                    DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Skill);
                }

                if (burnDuration > FP._0)
                {
                    StatusEffectUtility.ApplyBurn(f, target, burnDuration, burnIntensity, owner, DamageSource.Skill, config.TickInterval);
                }
            }
        }
    }
}
