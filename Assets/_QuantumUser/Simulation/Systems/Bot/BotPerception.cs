namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // One pass over the enemies around a bot, taken once per tick by BotInputSystem and shared by
    // goal selection, combat positioning and the skill rules (BotSkillUtility), so none of them
    // re-scan the enemy list on their own.
    public unsafe struct BotPerception
    {
        // Enemies further than this are invisible to the bot's tactical decisions (the map-wide
        // Elite/Explore searches in BotInputSystem don't use this snapshot).
        public static readonly FP Range = 18;

        public struct EnemyInfo
        {
            public EntityRef Entity;
            public FPVector3 Position;
            public FP Distance;
            public bool IsElite;
        }

        // Scratch buffer, cleared at the start of every Gather and only ever read within the same
        // BotInputSystem.Update - never carried across ticks, so it isn't simulation state and can't
        // desync or need rolling back. Static only to avoid a per-tick allocation.
        private static readonly List<EnemyInfo> Scratch = new List<EnemyInfo>(64);

        public List<EnemyInfo> Enemies => Scratch;

        public FPVector3 Position;
        public FP HealthFraction;

        public EntityRef NearestEnemy;
        public FP NearestEnemyDistance;

        // Sum of (away-from-enemy) unit vectors weighted by closeness within ThreatRadius - points
        // where it is safest to step. Zero when nothing is close.
        public FPVector2 ThreatDirection;
        public static readonly FP ThreatRadius = 6;

        public static BotPerception Gather(Frame f, EntityRef self, FPVector3 position)
        {
            Scratch.Clear();

            var perception = new BotPerception
            {
                Position = position,
                HealthFraction = FP._1,
                NearestEnemy = EntityRef.None,
                NearestEnemyDistance = FP.UseableMax,
            };

            if (f.Unsafe.TryGetPointer<Health>(self, out var health) == true && health->MaxHealth > FP._0)
            {
                perception.HealthFraction = health->CurrentHealth / health->MaxHealth;
            }

            FPVector2 threat = default;
            var enemies = f.Filter<Enemy, Transform3D>();

            while (enemies.Next(out EntityRef entity, out Enemy enemy, out Transform3D transform))
            {
                // Same exclusions as EnemyMovementUtility.TryFindNearestEnemy - a corpse in its death
                // animation or a burrowed/invulnerable enemy isn't a target.
                if (enemy.Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(entity) == true)
                    continue;

                FP distance = BotNavigation.FlatDistance(position, transform.Position);

                if (distance > Range)
                    continue;

                EnemyDataAsset data = f.FindAsset(enemy.EnemyData);
                bool isElite = data != null && (data.Tier == EnemyTier.Elite || data.Tier == EnemyTier.Boss);

                Scratch.Add(new EnemyInfo { Entity = entity, Position = transform.Position, Distance = distance, IsElite = isElite });

                if (distance < perception.NearestEnemyDistance)
                {
                    perception.NearestEnemy = entity;
                    perception.NearestEnemyDistance = distance;
                }

                if (distance < ThreatRadius && distance > FP._0_01)
                {
                    FPVector2 away = new FPVector2(position.X - transform.Position.X, position.Z - transform.Position.Z) / distance;
                    threat += away * (FP._1 - distance / ThreatRadius);
                }
            }

            perception.ThreatDirection = threat.SqrMagnitude > FP._0_01 ? threat.Normalized : default;
            return perception;
        }

        public bool HasEnemies => NearestEnemy != EntityRef.None;

        public int CountWithin(FP radius)
        {
            int count = 0;

            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].Distance <= radius)
                    count++;
            }

            return count;
        }

        public bool EliteWithin(FP radius)
        {
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].IsElite == true && Scratch[i].Distance <= radius)
                    return true;
            }

            return false;
        }

        // Enemies within `radius` of an arbitrary point (e.g. an aimed skill's target) - a cluster test.
        public int CountNear(FPVector3 point, FP radius)
        {
            int count = 0;

            for (int i = 0; i < Scratch.Count; i++)
            {
                if (BotNavigation.FlatDistance(point, Scratch[i].Position) <= radius)
                    count++;
            }

            return count;
        }

        public bool IsElite(EntityRef entity)
        {
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].Entity == entity)
                    return Scratch[i].IsElite;
            }

            return false;
        }
    }
}
