namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives BossTornado hazards (TornadoCastDeliveryData): a readable curved floor path (constant turn rate),
    // stops at a wall/void/step instead of crossing it, floor-gated periodic damage + a gentle escapable pull on
    // PLAYERS, then at natural expiry a compression tell and ONE pulse. Owner dead/gone = dissipates harmlessly
    // (no pulse); a stagger break cancels through BossHazardUtility.CancelOwnedHazards, same harmless path.
    // Self-contained on purpose - never touches the hero Vortex (enemy-targeting, Yoru upgrades).
    [Preserve]
    public unsafe class BossTornadoSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            List<EntityRef> doomed = null;
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);

            var tornadoes = f.Filter<BossTornado, Transform3D>();
            while (tornadoes.NextUnsafe(out EntityRef entity, out BossTornado* tornado, out Transform3D* transform))
            {
                TornadoCastDeliveryData config = f.FindAsset(tornado->Config);

                if (config == null || BossHazardUtility.IsOwnerAlive(f, tornado->Owner) == false)
                {
                    f.Events.BossTornadoDissipated(entity, transform->Position);
                    (doomed ??= new List<EntityRef>()).Add(entity);
                    continue;
                }

                if (tornado->Compressing == true)
                {
                    tornado->CompressTimer -= f.DeltaTime;

                    if (tornado->CompressTimer <= FP._0)
                    {
                        Pulse(f, entity, tornado, transform, config);
                        (doomed ??= new List<EntityRef>()).Add(entity);
                    }

                    continue;
                }

                tornado->Age += f.DeltaTime;

                if (tornado->Stopped == false)
                    Move(f, tornado, transform, config, groundMask);

                FP groundY = transform->Position.Y;
                Pull(f, transform->Position, groundY, config);

                tornado->DamageTimer -= f.DeltaTime;
                if (tornado->DamageTimer <= FP._0)
                {
                    tornado->DamageTimer += config.DamageInterval;
                    Damage(f, entity, tornado, transform->Position, groundY, config.Radius, tornado->TickDamage, config);
                }

                if (tornado->Age >= config.Lifetime)
                {
                    tornado->Compressing = true;
                    tornado->CompressTimer = config.CompressTell;
                }
            }

            if (doomed == null)
                return;

            for (int i = 0; i < doomed.Count; i++)
                f.Destroy(doomed[i]);
        }

        private static void Move(Frame f, BossTornado* tornado, Transform3D* transform, TornadoCastDeliveryData config, int groundMask)
        {
            // Rotate the heading by the turn rate (flat), then take one step along it.
            FP turn = tornado->TurnRate * f.DeltaTime * FP.Deg2Rad;
            FP cos = FPMath.Cos(turn);
            FP sin = FPMath.Sin(turn);
            FPVector2 h = tornado->Heading;
            tornado->Heading = new FPVector2(h.X * cos - h.Y * sin, h.X * sin + h.Y * cos);

            FPVector3 position = transform->Position;
            FP step = config.Speed * f.DeltaTime;

            // Never crosses a wall, void or ledge: blocked ahead -> deflect (deterministic order, turning the way it was
            // already curving first) and keep going; boxed in on every side -> holds its spot until it expires.
            for (int i = 0; i < DeflectAngles.Length; i++)
            {
                FP angle = DeflectAngles[i] * (tornado->TurnRate >= FP._0 ? FP._1 : -FP._1);
                FPVector2 heading = i == 0 ? tornado->Heading : Rotate(tornado->Heading, angle * FP.Deg2Rad);

                if (TryStep(f, position, heading, step, config, groundMask, out FPVector3 next) == false)
                    continue;

                if (i > 0)
                {
                    tornado->Heading = heading;
                    tornado->TurnRate = -tornado->TurnRate; // curl back the other way after bouncing off
                }

                transform->Position = next;
                return;
            }

            tornado->Stopped = true;
        }

        private static readonly FP[] DeflectAngles = { 0, 60, -60, 120, -120 };

        private static FPVector2 Rotate(FPVector2 v, FP radians)
        {
            FP cos = FPMath.Cos(radians);
            FP sin = FPMath.Sin(radians);
            return new FPVector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private static bool TryStep(Frame f, FPVector3 position, FPVector2 heading, FP step, TornadoCastDeliveryData config, int groundMask, out FPVector3 next)
        {
            FPVector3 direction = new FPVector3(heading.X, FP._0, heading.Y);
            next = position + direction * step;

            // Probe from body height so a low curb doesn't count as a wall; a little look-ahead keeps it off wall faces.
            FPVector3 probeOrigin = new FPVector3(position.X, position.Y + FP._0_50, position.Z);
            if (EnemyMovementUtility.IsBlockedByWall(f, probeOrigin, direction, step + FP._0_50, groundMask) == true)
                return false;

            if (EnemyMovementUtility.TryFindGroundHeight(f, next, groundMask, out FP groundY) == false ||
                FPMath.Abs(groundY - position.Y) > config.MaxStepHeight)
                return false;

            next.Y = groundY;
            return true;
        }

        // Gentle and escapable: a fixed per-tick displacement toward the centre (KCC external delta - collision
        // resolved, friction-independent, never builds up into velocity), well under walking speed. Skips Downed/KO
        // and players on another floor.
        private static void Pull(Frame f, FPVector3 center, FP groundY, TornadoCastDeliveryData config)
        {
            if (config.PullSpeed <= FP._0 || config.PullRadius <= FP._0)
                return;

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int count = BossHazardUtility.GatherPlayersOnFloor(f, center, groundY, config.PullRadius, config.MaxHeightDifference, players);

            for (int i = 0; i < count; i++)
            {
                if (f.Unsafe.TryGetPointer<KCC>(players[i], out var kcc) == false || f.Unsafe.TryGetPointer<Transform3D>(players[i], out var transform) == false)
                    continue;

                FPVector3 toCenter = new FPVector3(center.X - transform->Position.X, FP._0, center.Z - transform->Position.Z);
                FP distance = toCenter.Magnitude;

                if (distance <= FP._0_25)
                    continue;

                kcc->AddExternalDelta(toCenter / distance * FPMath.Min(config.PullSpeed * f.DeltaTime, distance));
            }
        }

        private static void Damage(Frame f, EntityRef entity, BossTornado* tornado, FPVector3 center, FP groundY, FP radius, FP damage, TornadoCastDeliveryData config)
        {
            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayerLayerCandidates];
            int count = BossHazardUtility.GatherPlayersOnFloor(f, center, groundY, radius, config.MaxHeightDifference, players);

            for (int i = 0; i < count; i++)
            {
                if (f.Unsafe.TryGetPointer<Transform3D>(players[i], out var transform) == false)
                    continue;

                HitEffectContext context = new HitEffectContext
                {
                    Owner = tornado->Owner,
                    Target = players[i],
                    SourceEntity = entity,
                    Position = transform->Position,
                    PushDirection = transform->Position - center,
                    Damage = damage,
                    Source = DamageSource.None,
                    Element = ElementType.Neutral,
                };

                HitEffectUtility.ApplyToTarget(f, config.Effects, ref context, multiTarget: true);
            }
        }

        private static void Pulse(Frame f, EntityRef entity, BossTornado* tornado, Transform3D* transform, TornadoCastDeliveryData config)
        {
            FPVector3 center = transform->Position;
            Damage(f, entity, tornado, center, center.Y, config.PulseRadius, config.PulseDamage, config);
            f.Events.BossTornadoPulse(entity, center, config.PulseRadius);
        }
    }
}
