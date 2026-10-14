namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives every SpikeTrap's cycle (see SpikeTrap.qtn). Targets are scanned directly (players by
    // PlayerLink, enemies by Enemy) with a plain X/Z box + height test - no physics query - and every
    // hit goes through DamageUtility.ApplyDamage, which already skips dead, Invulnerable (downed,
    // burrowed, grace) and friendly-fire cases. What's specific to a FLOOR hazard is filtered here:
    // airborne players (a jump clears the spikes), non-grounded/flying enemies, burrowed enemies and
    // EnemyTrait.GroundHazardImmunity.
    [Preserve]
    public unsafe class SpikeTrapSystem : SystemMainThreadFilter<SpikeTrapSystem.Filter>
    {
        // A target this far above/below the plate (different platform level) is never on it.
        private static readonly FP MaxHeightDifference = FP._1 + FP._0_50;

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public SpikeTrap* Trap;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            SpikeTrap* trap = filter.Trap;

            if (trap->Initialized == false)
            {
                trap->Initialized = true;
                trap->Phase = SpikeTrapPhase.Armed;
                // Timer traps start TimerOffset "behind", so a row of them can ripple instead of firing in sync.
                trap->PhaseTimer = -trap->TimerOffset;
            }

            trap->PhaseTimer += f.DeltaTime;

            // Zero-length phases chain through in the same tick (e.g. WarningTime 0 = spikes on the step).
            for (int guard = 0; guard < 4; guard++)
            {
                if (Advance(f, filter.Entity, filter.Transform3D->Position, trap) == false)
                    break;
            }

            if (trap->Phase == SpikeTrapPhase.Extended)
            {
                trap->HitTimer -= f.DeltaTime;

                if (trap->HitTimer <= FP._0)
                {
                    trap->HitTimer = trap->HitInterval > FP._0 ? trap->HitInterval : trap->ExtendedTime;
                    HitEverythingOnPlate(f, filter.Entity, filter.Transform3D->Position, trap);
                }
            }
        }

        // Returns true when the phase changed (so the caller can let a zero-length phase chain on).
        private static bool Advance(Frame f, EntityRef entity, FPVector3 center, SpikeTrap* trap)
        {
            switch (trap->Phase)
            {
                case SpikeTrapPhase.Armed:
                    bool fire = trap->Trigger == SpikeTrapTrigger.Timer
                        ? trap->PhaseTimer >= trap->TimerArmedTime
                        : AnyTargetOnPlate(f, entity, center, trap);

                    return fire && Enter(trap, SpikeTrapPhase.Warning);

                case SpikeTrapPhase.Warning:
                    if (trap->PhaseTimer < trap->WarningTime)
                        return false;

                    trap->HitTimer = FP._0; // first hit the instant the spikes are up
                    return Enter(trap, SpikeTrapPhase.Extended);

                case SpikeTrapPhase.Extended:
                    return trap->PhaseTimer >= trap->ExtendedTime && Enter(trap, SpikeTrapPhase.Cooldown);

                case SpikeTrapPhase.Cooldown:
                    return trap->PhaseTimer >= trap->CooldownTime && Enter(trap, SpikeTrapPhase.Armed);
            }

            return false;
        }

        private static bool Enter(SpikeTrap* trap, SpikeTrapPhase phase)
        {
            trap->Phase = phase;
            trap->PhaseTimer = FP._0;
            return true;
        }

        private static bool AnyTargetOnPlate(Frame f, EntityRef trapEntity, FPVector3 center, SpikeTrap* trap)
        {
            if (trap->TargetMask != DamageTargetMask.Enemies)
            {
                var players = f.Filter<PlayerLink, Transform3D>();

                while (players.Next(out EntityRef player, out PlayerLink _, out Transform3D transform))
                {
                    if (IsOnPlate(transform.Position, center, trap) && IsValidPlayer(f, player, trap))
                        return true;
                }
            }

            if (trap->TargetMask != DamageTargetMask.Players && trap->TeachingHit == false)
            {
                var enemies = f.Filter<Enemy, Transform3D>();

                while (enemies.Next(out EntityRef enemy, out Enemy _, out Transform3D transform))
                {
                    if (IsOnPlate(transform.Position, center, trap) && IsValidEnemy(f, enemy))
                        return true;
                }
            }

            return false;
        }

        private static void HitEverythingOnPlate(Frame f, EntityRef trapEntity, FPVector3 center, SpikeTrap* trap)
        {
            if (trap->TargetMask != DamageTargetMask.Enemies)
            {
                var players = f.Filter<PlayerLink, Transform3D>();

                while (players.Next(out EntityRef player, out PlayerLink _, out Transform3D transform))
                {
                    if (IsOnPlate(transform.Position, center, trap) && IsValidPlayer(f, player, trap))
                        DamageUtility.ApplyDamage(f, player, trap->Damage, trapEntity);
                }
            }

            if (trap->TargetMask != DamageTargetMask.Players && trap->TeachingHit == false)
            {
                var enemies = f.Filter<Enemy, Transform3D>();

                while (enemies.Next(out EntityRef enemy, out Enemy _, out Transform3D transform))
                {
                    if (IsOnPlate(transform.Position, center, trap) && IsValidEnemy(f, enemy))
                        DamageUtility.ApplyDamage(f, enemy, trap->Damage, trapEntity);
                }
            }
        }

        private static bool IsOnPlate(FPVector3 position, FPVector3 center, SpikeTrap* trap)
        {
            FPVector3 offset = position - center;

            return FPMath.Abs(offset.X) <= trap->HalfExtents.X
                && FPMath.Abs(offset.Z) <= trap->HalfExtents.Y
                && FPMath.Abs(offset.Y) <= MaxHeightDifference;
        }

        private static bool IsValidPlayer(Frame f, EntityRef player, SpikeTrap* trap)
        {
            // Airborne (jumping / launched) clears the spikes.
            if (f.Unsafe.TryGetPointer<KCC>(player, out var kcc) && kcc->Data.IsGrounded == false)
                return false;

            if (trap->TeachingHit == false)
                return true;

            return f.Unsafe.TryGetPointer<AccessoryGuard>(player, out var guard)
                && guard->Disabled == false
                && guard->State == AccessoryGuardState.Equipped
                && guard->CurrentDurability > 1;
        }

        private static bool IsValidEnemy(Frame f, EntityRef enemy)
        {
            if (f.Has<Burrowed>(enemy))
                return false;

            if (f.Unsafe.TryGetPointer<Enemy>(enemy, out var enemyComponent) == false || enemyComponent->EnemyData.IsValid == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemyComponent->EnemyData);

            return data.Stats.Height.InitialState == EnemyHeightState.Grounded
                && data.Stats.HasTrait(EnemyTrait.GroundHazardImmunity) == false;
        }
    }
}
