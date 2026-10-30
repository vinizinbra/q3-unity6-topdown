namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Throws moving floor tornadoes (BossTornado, driven by BossTornadoSystem) - Dono do Céu's "Vendaval da
    // Geral". Spawns Count tornadoes one SpawnInterval apart (the first in Begin, the rest in Tick, so the boss
    // holds its release pose for the whole cast), each launched SideAngle off the aim on alternating sides and
    // curving back across the arena at TurnRate (alternating sign). Hard cap: never more than MaxActive alive per
    // owner, whatever the phase or cooldown - CanBegin refuses while ANY is alive, so a cast never stacks a pair.
    //
    // This asset is also each tornado's tuning (BossTornado.Config points back at it). Player-only: damage,
    // pull and the expiry pulse all go through floor-gated player queries; nothing here touches Yoru's Vortex.
    public unsafe class TornadoCastDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<EntityPrototype> TornadoPrototype;

        public int Count = 2;
        public int MaxActive = 2;
        public FP SpawnInterval = FP.FromString("0.45");
        public FP SpawnDistance = FP.FromString("2.5");
        public FP SideAngle = 35;

        [UnityEngine.Header("Movement")]
        public FP Speed = FP.FromString("2.6");
        public FP TurnRate = 38;
        // Max floor step a tornado can follow per tick - more (a ledge/void) and it stops where it is.
        public FP MaxStepHeight = FP.FromString("0.6");

        [UnityEngine.Header("Lifetime")]
        public FP Lifetime = 7;
        public FP CompressTell = FP.FromString("0.7");

        [UnityEngine.Header("Damage")]
        public FP Radius = FP.FromString("1.6");
        public FP DamageInterval = FP._0_50;
        // Floor gate: a player whose floor is further than this from the tornado's floor is never touched.
        public FP MaxHeightDifference = FP.FromString("1.2");

        [UnityEngine.Header("Pull")]
        public FP PullRadius = FP.FromString("3.4");
        // World units per second toward the centre - well under player run speed, so walking out always works.
        public FP PullSpeed = FP.FromString("1.6");

        [UnityEngine.Header("Expiry pulse")]
        public FP PulseRadius = FP.FromString("2.6");
        public FP PulseDamage = 20;

        // Tick hits use action.Damage; both tick and pulse apply these effects (damage effect = deals it).
        [ExpandableAsset] public List<AssetRef<HitEffectData>> Effects = new();

        // Gate shared by the boss's big mechanics - see BossHazardUtility.IsMajorMechanicActive.
        public bool BlockWhileMajorMechanicActive = true;

        public override bool CanBegin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            if (BossHazardUtility.CountTornadoes(f, filter.Entity) > 0)
                return false;

            return BlockWhileMajorMechanicActive == false || BossHazardUtility.IsMajorMechanicActive(f, filter.Entity) == false;
        }

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->PendingImpactTotal = (byte)(Count < 0 ? 0 : (Count > 255 ? 255 : Count));
            filter.Enemy->PendingImpactIndex = 0;
            SpawnNext(f, ref filter, action.Damage);
            filter.Enemy->StateTimer = SpawnInterval;
            return filter.Enemy->PendingImpactIndex >= filter.Enemy->PendingImpactTotal;
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            SpawnNext(f, ref filter, action.Damage);
            filter.Enemy->StateTimer += SpawnInterval;
            return filter.Enemy->PendingImpactIndex >= filter.Enemy->PendingImpactTotal;
        }

        private void SpawnNext(Frame f, ref EnemySystem.Filter filter, FP tickDamage)
        {
            int index = filter.Enemy->PendingImpactIndex;
            filter.Enemy->PendingImpactIndex++;

            if (TornadoPrototype.IsValid == false || BossHazardUtility.CountTornadoes(f, filter.Entity) >= MaxActive)
                return;

            // Alternating sides: 0 -> left of the aim curving right, 1 -> right curving left, ...
            FP side = index % 2 == 0 ? FP._1 : -FP._1;
            FP angle = filter.Aim->Angle + SideAngle * side;
            FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;
            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 position = origin + direction * SpawnDistance;

            // Never spawn into a wall or over a void: fall back to the boss's own spot.
            int groundMask = EnemyMovementUtility.GetGroundLayerMask(f);
            if (EnemyMovementUtility.IsBlockedByWall(f, origin, direction, SpawnDistance, groundMask) == true ||
                EnemyMovementUtility.TryFindGroundHeight(f, position, groundMask, out FP groundY, filter.Entity) == false)
            {
                position = origin;
                groundY = EnemyMovementUtility.ResolveGroundY(f, origin);
            }

            position.Y = groundY;

            EntityRef tornado = f.Create(TornadoPrototype);
            if (f.Unsafe.TryGetPointer<Transform3D>(tornado, out var transform) == true)
                transform->Position = position;

            // AreaOwner = the boss: BossDefeatUtility.ClearArena already sweeps enemy-owned AreaOwner entities.
            f.AddOrGet<AreaOwner>(tornado, out var areaOwner);
            areaOwner->Owner = filter.Entity;

            f.AddOrGet<BossTornado>(tornado, out var state);
            state->Owner = filter.Entity;
            state->Config = new AssetRef<TornadoCastDeliveryData>(Guid);
            state->Heading = new FPVector2(direction.X, direction.Z);
            state->TurnRate = -TurnRate * side; // curve back toward the middle of the fan
            state->DamageTimer = DamageInterval;
            state->TickDamage = tickDamage;
        }

        public override void OnInterrupted(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action)
        {
            filter.Enemy->PendingImpactIndex = filter.Enemy->PendingImpactTotal; // no more spawns
        }
    }
}
