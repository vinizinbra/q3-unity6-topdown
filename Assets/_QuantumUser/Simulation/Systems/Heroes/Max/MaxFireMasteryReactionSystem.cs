namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Max's Fire Mastery traits - Flashpoint (crit-vs-Burning explosion, the only one needing the
    // Filter half for its own per-tick ProcCooldown ticking, same shape WeaponSystem.
    // TickKillerInstinct already uses), Cremation (execute-vs-Burning-and-low-Health), Wildfire
    // (any-Burning-death Burn spread). Independent of MaxVendettaSystem/Vendetta itself - none of
    // these three read RevengeConfig/RevengeMark, only their own Fire Mastery components (see
    // FireMastery.qtn). See docs/max-vendetta-fire-mastery.md.
    [Preserve]
    public unsafe class MaxFireMasteryReactionSystem : SystemMainThreadFilter<MaxFireMasteryReactionSystem.Filter>,
        ISignalOnCriticalHit, ISignalOnHealthDamageApplied, ISignalOnEntityKilled
    {
        public struct Filter
        {
            public EntityRef Entity;
            public ExplosionOnConditionalHit* Explosion;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Explosion->CooldownRemaining > FP._0)
            {
                filter.Explosion->CooldownRemaining -= f.DeltaTime;
            }
        }

        // Flashpoint - a crit against an already-Burning target detonates a capped-radius
        // explosion centered on it, on top of the crit's own direct damage. Uses AreaQueryUtility
        // directly (not HitEffectUtility.ApplyExplosion, which has no target cap) so MaxTargets is
        // actually honored, then fires the same WeaponExplosionReleased event ApplyExplosion would
        // have for the VFX hookup. Doesn't exclude the crit's own target from the blast - matches
        // HitEffectUtility.ApplyDamageInRadius's own convention of only ever excluding owner.
        public void OnCriticalHit(Frame f, EntityRef target, EntityRef owner, FP damage, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<ExplosionOnConditionalHit>(owner, out var explosion) == false
                || explosion->CooldownRemaining > FP._0)
                return;

            if (StatusEffectUtility.IsBurning(f, target) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            // Set before dealing damage, unless AllowRecursiveProc opts a rank into chaining - this
            // is what blocks same-tick reentrancy if the explosion's own damage crits a Burning
            // enemy again (see docs/max-vendetta-fire-mastery.md's edge case table).
            if (explosion->AllowRecursiveProc == false)
            {
                explosion->CooldownRemaining = explosion->ProcCooldown;
            }

            var targets = AreaQueryUtility.FindEnemiesInRadius(f, transform->Position, explosion->Radius, owner, explosion->MaxTargets);
            FP explosionDamage = damage * explosion->DamageCoefficient;

            for (int i = 0; i < targets.Count; i++)
            {
                DamageUtility.ApplyDamage(f, targets[i], explosionDamage, owner, source);
            }

            f.Events.WeaponExplosionReleased(owner, transform->Position, explosion->Radius);

            if (explosion->AllowRecursiveProc == true)
            {
                explosion->CooldownRemaining = explosion->ProcCooldown;
            }
        }

        // Cremation - forces a lethal Health hit the rest of the way to 0 if target is Burning and
        // already at/below its own tier's execute threshold. Runs inline inside ApplyDamage's own
        // OnHealthDamageApplied window (fired after Health is already reduced, before ApplyDamage's
        // death check), so the execution flows through the existing death branch unmodified -
        // events, drops, OnEntityKilled all fire exactly as they do for a normal kill.
        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            if (f.Unsafe.TryGetPointer<ExecuteAgainstStatus>(owner, out var execute) == false)
                return;

            if (StatusEffectUtility.IsBurning(f, target) == false)
                return;

            if (f.Unsafe.TryGetPointer<Health>(target, out var health) == false || health->CurrentHealth <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            FP threshold = ResolveExecuteThreshold(execute, data.Tier, out bool eligible);

            if (eligible == false || health->CurrentHealth > health->MaxHealth * threshold)
                return;

            health->CurrentHealth = FP._0;
            Log.Debug($"[FireMastery] {owner}'s Cremation executed {target} (tier {data.Tier})");
        }

        // Filler/Normal/Specialist/Heavy all share the "Normal" bucket - Cremation only authors 3
        // threshold tiers (see docs/max-vendetta-fire-mastery.md's "verify before implementing"
        // note), so every sub-Elite tier reads off NormalHealthThreshold rather than needing its
        // own field. Boss additionally requires BossExecutionEnabled so a lower rank can't
        // accidentally execute a Boss just by authoring a nonzero BossHealthThreshold default.
        private static FP ResolveExecuteThreshold(ExecuteAgainstStatus* execute, EnemyTier tier, out bool eligible)
        {
            eligible = true;

            switch (tier)
            {
                case EnemyTier.Boss:
                    eligible = execute->BossExecutionEnabled;
                    return execute->BossHealthThreshold;
                case EnemyTier.Elite:
                    return execute->EliteHealthThreshold;
                default:
                    return execute->NormalHealthThreshold;
            }
        }

        // Wildfire - any kill while Burning spreads Burn to nearby enemies, independent of whether
        // this was Max's own Vendetta-marked kill (see MaxVendettaSystem.OnEntityKilled for Burning
        // Vengeance's own Vendetta-scoped spread - both share StatusSpreadOnDeath and
        // FireMasterySpreadUtility.SpreadBurn). Read before any deferred destroy - see ApplyDamage,
        // which fires OnEntityKilled before ever touching the target's own lifetime.
        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<StatusSpreadOnDeath>(owner, out var spread) == false || spread->TriggerOnAnyBurningDeath == false)
                return;

            if (StatusEffectUtility.IsBurning(f, target) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            FP burnDuration = spread->BurnDuration;
            FP burnIntensity = spread->BurnIntensity;

            // Wildfire rank 3 - spread a retained fraction of the dying enemy's OWN live Burn instead
            // of the flat authored values, so a fire that's already burning hot/long propagates that
            // same intensity/duration onward (scaled down) rather than resetting every jump.
            if (spread->WildfireRetainedFraction > FP._0
                && f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == true)
            {
                burnDuration = FPMath.Max(burnDuration, status->BurnRemaining * spread->WildfireRetainedFraction);
                burnIntensity = FPMath.Max(burnIntensity, status->BurnDamagePerTick * spread->WildfireRetainedFraction);
            }

            FireMasterySpreadUtility.SpreadBurn(f, transform->Position, owner, target, spread->Radius, burnDuration, burnIntensity, spread->MaxTargets);
        }
    }
}
