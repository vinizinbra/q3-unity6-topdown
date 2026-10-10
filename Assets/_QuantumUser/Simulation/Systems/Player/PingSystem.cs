namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Resolves a Ping button press into one PingPlaced event (see docs/ping.md and Ping.qtn).
    // Priority: own Downed -> Elite in view -> nearest ping-able POI -> empty ground.
    [Preserve]
    public unsafe class PingSystem : SystemMainThreadFilter<PingSystem.Filter>
    {
        // Placeholder tuning - candidates for a config asset once the feel is settled.
        private static readonly FP Cooldown = FP._0_50;
        private static readonly FP StickCooldown = (FP)10;
        private static readonly FP SpamWindow = (FP)3;
        private const int SpamCountForStick = 3;
        private static readonly FP GroundDistance = (FP)6;
        // Gameplay camera is FOV 14 / Y 36, so roughly this far is on screen around the player.
        private static readonly FP EliteViewRadius = (FP)12;
        private static readonly FP EliteConeDot = FP._0_75;
        private static readonly FP PoiPickRadius = (FP)4;

        public override void Update(Frame f, ref Filter filter)
        {
            f.AddOrGet<PlayerPing>(filter.Entity, out var ping);

            ping->Cooldown = FPMath.Max(FP._0, ping->Cooldown - f.DeltaTime);
            ping->StickCooldown = FPMath.Max(FP._0, ping->StickCooldown - f.DeltaTime);
            if (ping->GroundSpamTimer > FP._0)
            {
                ping->GroundSpamTimer -= f.DeltaTime;
                if (ping->GroundSpamTimer <= FP._0)
                    ping->GroundSpamCount = 0;
            }

            if (PlayerInputUtility.IsBot(f, filter.Entity) == true)
                return;

            var input = PlayerInputUtility.Resolve(f, filter.Entity, filter.PlayerLink);
            if (input == null || input->Ping.WasPressed == false)
                return;

            if (ping->Cooldown > FP._0)
                return;

            var lifeState = f.Unsafe.TryGetPointer<PlayerLifeState>(filter.Entity, out var life) ? life->State : PlayerLifeStateKind.Alive;
            if (lifeState == PlayerLifeStateKind.KO)
                return;

            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 position = origin;
            EntityRef target = EntityRef.None;
            ContextInteractionState poiState = ContextInteractionState.None;
            PingKind kind;

            if (lifeState == PlayerLifeStateKind.Downed)
            {
                kind = PingKind.ReviveMe;
            }
            else
            {
                FP angle = filter.Aim->Angle * FP.Deg2Rad;
                FPVector2 dir = new FPVector2(FPMath.Sin(angle), FPMath.Cos(angle));

                kind = TryResolveElite(f, filter.Aim, origin, dir, out target, out position) ? PingKind.Elite
                    : TryResolvePoi(f, filter.Entity, origin, dir, out kind, out target, out position, out poiState) ? kind
                    : PingKind.Ground;

                if (kind == PingKind.Ground)
                {
                    position = new FPVector3(origin.X + dir.X * GroundDistance, origin.Y, origin.Z + dir.Y * GroundDistance);

                    ping->GroundSpamTimer = SpamWindow;
                    ping->GroundSpamCount++;
                    if (ping->GroundSpamCount >= SpamCountForStick && ping->StickCooldown <= FP._0)
                    {
                        kind = PingKind.StickTogether;
                        position = origin; // "come to me" - marks the pinger, not the ground ahead
                        ping->StickCooldown = StickCooldown;
                        ping->GroundSpamCount = 0;
                    }
                }
                else
                {
                    ping->GroundSpamCount = 0;
                    ping->GroundSpamTimer = FP._0;
                }
            }

            ping->Cooldown = Cooldown;
            f.Events.PingPlaced(filter.PlayerLink->Player, filter.Entity, kind, position, target, poiState);
        }

        private static bool TryResolveElite(Frame f, Aim* aim, FPVector3 origin, FPVector2 dir, out EntityRef target, out FPVector3 position)
        {
            target = EntityRef.None;
            position = default;

            FP radiusSqr = EliteViewRadius * EliteViewRadius;

            // Locked/auto target first: "aiming at it and damaging it".
            EntityRef aimed = aim->LockedTarget != EntityRef.None ? aim->LockedTarget : aim->Target;
            if (IsLiveElite(f, aimed) && f.Unsafe.TryGetPointer<Transform3D>(aimed, out var aimedTransform)
                && EnemyMovementUtility.FlatSqrDistance(origin, aimedTransform->Position) <= radiusSqr)
            {
                target = aimed;
                position = aimedTransform->Position;
                return true;
            }

            // Otherwise the nearest elite in front of the player within view range.
            FP bestSqr = FP.UseableMax;
            var enemies = f.Filter<Enemy, Transform3D>();
            while (enemies.Next(out EntityRef candidate, out Enemy enemy, out Transform3D t))
            {
                if (IsLiveElite(f, candidate) == false)
                    continue;

                FPVector2 delta = new FPVector2(t.Position.X - origin.X, t.Position.Z - origin.Z);
                FP sqr = delta.SqrMagnitude;
                if (sqr > radiusSqr || sqr >= bestSqr || sqr == FP._0)
                    continue;

                if (FPVector2.Dot(delta / FPMath.Sqrt(sqr), dir) < EliteConeDot)
                    continue;

                bestSqr = sqr;
                target = candidate;
                position = t.Position;
            }

            return target != EntityRef.None;
        }

        private static bool IsLiveElite(Frame f, EntityRef entity)
        {
            if (entity == EntityRef.None || f.Exists(entity) == false)
                return false;
            if (f.Unsafe.TryGetPointer<Enemy>(entity, out var enemy) == false)
                return false;
            if (enemy->Phase == EnemyActionPhase.Dead)
                return false;

            var data = f.FindAsset(enemy->EnemyData);
            return data != null && data.Tier == EnemyTier.Elite;
        }

        // Nearest ping-able Interactable to either the player or the point they are aiming at.
        private static bool TryResolvePoi(Frame f, EntityRef player, FPVector3 origin, FPVector2 dir, out PingKind kind, out EntityRef target, out FPVector3 position, out ContextInteractionState state)
        {
            state = ContextInteractionState.None;
            kind = PingKind.None;
            target = EntityRef.None;
            position = default;

            FPVector3 aimPoint = new FPVector3(origin.X + dir.X * GroundDistance, origin.Y, origin.Z + dir.Y * GroundDistance);
            FP bestSqr = FP.UseableMax;
            InteractableKind poiKind = default;

            var pois = f.Filter<Interactable, Transform3D>();
            while (pois.Next(out EntityRef candidate, out Interactable interactable, out Transform3D t))
            {
                PingKind candidateKind = ResolvePoiKind(f, candidate, interactable.Kind);
                if (candidateKind == PingKind.None)
                    continue;

                FP nearPlayer = EnemyMovementUtility.FlatSqrDistance(origin, t.Position);
                FP nearAim = EnemyMovementUtility.FlatSqrDistance(aimPoint, t.Position);
                FP playerReach = FPMath.Max(interactable.Radius, PoiPickRadius);

                bool viaPlayer = nearPlayer <= playerReach * playerReach;
                bool viaAim = nearAim <= PoiPickRadius * PoiPickRadius;
                if (viaPlayer == false && viaAim == false)
                    continue;

                FP score = FPMath.Min(nearPlayer, nearAim);
                if (score >= bestSqr)
                    continue;

                bestSqr = score;
                kind = candidateKind;
                target = candidate;
                position = t.Position;
                poiKind = interactable.Kind;
            }

            if (kind != PingKind.None)
                state = ContextInteractionSystem.ResolveState(f, player, target, poiKind);

            return kind != PingKind.None;
        }

        private static PingKind ResolvePoiKind(Frame f, EntityRef poi, InteractableKind interactable)
        {
            switch (interactable)
            {
                case InteractableKind.Store: return PingKind.Store;
                case InteractableKind.Blacksmith: return PingKind.Blacksmith;
                case InteractableKind.HealingShrine: return PingKind.Shrine;
                case InteractableKind.CursedRift: return PingKind.Rift;
                case InteractableKind.TraversalChallenge: return PingKind.Traversal;
                case InteractableKind.TeamChallenge:
                    if (f.Unsafe.TryGetPointer<TeamChallenge>(poi, out var challenge) == false)
                        return PingKind.None;
                    switch (challenge->State)
                    {
                        case TeamChallengeState.Available:
                        case TeamChallengeState.WaitingForTeam:
                            return PingKind.TeamChallengeFound;
                        case TeamChallengeState.Starting:
                        case TeamChallengeState.ChallengeActive:
                            return PingKind.TeamChallengeStart;
                        case TeamChallengeState.RewardAvailable:
                            return PingKind.TeamChallengeReward;
                        case TeamChallengeState.Completed:
                            return PingKind.TeamChallengeDone;
                        case TeamChallengeState.Failed:
                            return PingKind.TeamChallengeFailed;
                        default:
                            return PingKind.None;
                    }
                default:
                    return PingKind.None;
            }
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
            public Transform3D* Transform3D;
            public Aim* Aim;
        }
    }
}
