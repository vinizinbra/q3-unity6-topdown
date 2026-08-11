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
                ApplyRadialBurn(f, transform->Position, ignition->InfernoRadius, owner, FP._0,
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

        // Damage + Burn to every enemy in radius - Ignition rank 3's Inferno pulse and Burning
        // Vengeance rank 3's fiery burst both call this instead of each re-deriving the same
        // OverlapShape/Enemy-gate loop. damage may be 0 to apply Burn only (Inferno's own pulse -
        // the ignited Burn tick is the actual damage here, not a separate upfront hit). Mirrors
        // BruteAscensionUtility.ApplyRadialStunDamage's exact shape.
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
