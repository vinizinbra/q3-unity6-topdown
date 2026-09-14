namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum;
    using static BalanceSimAssets;

    // Analytical mirror of SurvivalProgressionUtility.Tick + CombatDirectorUtility.TryPulse/
    // TrySelectSpawn for a cohesive party (one front). Split fronts, encounter modifiers and rift
    // mutations are treated as 1x.
    public class BalanceSimDirector
    {
        private readonly BalanceSimAssets assets;
        private readonly BalanceSimScenario scenario;
        private readonly SimRng rng;
        private readonly int playerCount;

        public int PhaseIndex;
        public double SurvivalTime;
        public double PhaseTimer;
        public double PulseTimer;
        public double Budget;
        public double RunTime;
        public int BreathingIndex = -1;
        public bool BossReached;
        public bool GuaranteedDone;
        public readonly List<SimEnemy> Alive = new();
        public int SpawnedTotal;
        public int RetiredTotal;
        public double BudgetGranted;
        public double BudgetSpent;
        public int FailedPurchases;

        private readonly Dictionary<AssetObject, int> aliveBySource = new();

        public BalanceSimDirector(BalanceSimAssets assets, BalanceSimScenario scenario, int playerCount, SimRng rng)
        {
            this.assets = assets;
            this.scenario = scenario;
            this.playerCount = playerCount;
            this.rng = rng;
        }

        public SurvivalPhase Phase => assets.Survival.Phases[Math.Min(PhaseIndex, assets.Survival.Phases.Length - 1)];
        public string PhaseName => string.IsNullOrEmpty(Phase.Name) ? $"Phase {PhaseIndex}" : Phase.Name;
        public double Pressure { get { double p = 0; foreach (SimEnemy e in Alive) p += e.Cost; return p; } }
        public double CoopPressure => D(assets.Balance.GetCoopGlobal(CoopGlobalKey.DirectorPressure, playerCount));
        public double TargetPressure => D(Phase.TargetPressure) * CoopPressure;

        public double EnemyHpMultiplier(EnemyTier tier)
            => D(assets.Balance.EvaluateEnemyHp(tier, FP.FromFloat_UNSAFE((float)SurvivalTime))) * D(assets.Balance.GetCoopHp(tier, playerCount));

        public double EnemyDamageMultiplier
            => D(assets.Balance.Evaluate(CurveChannel.EnemyDmg, FP.FromFloat_UNSAFE((float)SurvivalTime))) * D(assets.Balance.GetCoopGlobal(CoopGlobalKey.EnemyDamage, playerCount));

        public double ExpectedPlayerDpsCurve
            => D(assets.Balance.Evaluate(CurveChannel.ExpectedPlayerDps, FP.FromFloat_UNSAFE((float)SurvivalTime)));

        // HP an enemy of this tier spawns with right now (EnemyBalanceUtility.ResolveEnemyStats).
        // Tier shield is per-enemy (Stats.ShieldMultiplier, 0 on every authored enemy) so it's
        // only added on the actual spawn, not here.
        public double EffectiveHp(EnemyTier tier)
            => Math.Round(D(assets.Tier(tier).MaxHealth) * EnemyHpMultiplier(tier));

        // One simulation tick. enteredBreak is raised on the tick a Breathing phase begins.
        public void Tick(double dt, out bool enteredBreak)
        {
            enteredBreak = false;
            RunTime += dt;
            SurvivalPhase phase = Phase;
            bool cleared = IsEncounterCleared(phase.Kind);
            bool freeze = phase.Kind == SurvivalPhaseKind.Breathing || (phase.Kind == SurvivalPhaseKind.Elite && cleared == false);

            if (freeze == false)
                SurvivalTime += dt;

            if (cleared)
                PhaseTimer += dt;

            bool isLast = PhaseIndex >= assets.Survival.Phases.Length - 1;

            if (isLast == false && cleared && PhaseTimer >= D(phase.Duration))
            {
                PhaseIndex++;
                PhaseTimer = 0;
                GuaranteedDone = false;
                phase = Phase;

                if (phase.Kind == SurvivalPhaseKind.Breathing)
                {
                    BreathingIndex++;
                    enteredBreak = true;
                }

                if (phase.Kind == SurvivalPhaseKind.Boss)
                    BossReached = true;
            }

            if (phase.Kind == SurvivalPhaseKind.Breathing || phase.Kind == SurvivalPhaseKind.Boss)
            {
                RetireLeakers();
                return;
            }

            if (GuaranteedDone == false)
            {
                SpawnGuaranteed(phase);
                GuaranteedDone = true;
            }

            TryPulse(phase, dt);
            RetireLeakers();
        }

        private bool IsEncounterCleared(SurvivalPhaseKind kind)
        {
            if (kind == SurvivalPhaseKind.Combat)
                return true;

            foreach (SimEnemy enemy in Alive)
            {
                if (kind == SurvivalPhaseKind.Breathing)
                    return false;

                EnemyTier required = kind == SurvivalPhaseKind.Elite ? EnemyTier.Elite : EnemyTier.Boss;
                if (enemy.Tier == required)
                    return false;
            }

            return true;
        }

        private void SpawnGuaranteed(SurvivalPhase phase)
        {
            EnemyGroupConfig group = assets.Resolve(phase.GuaranteedGroup);
            if (group != null)
                SpawnGroup(group);

            EnemyDataAsset enemy = assets.Resolve(phase.GuaranteedEnemyData);
            if (enemy != null)
                Spawn(enemy, enemy);
        }

        private double BudgetMultiplier
            => D(assets.Balance.Evaluate(CurveChannel.DirectorBudget, FP.FromFloat_UNSAFE((float)SurvivalTime)))
               * D(assets.Balance.GetCoopGlobal(CoopGlobalKey.DirectorBudget, playerCount));

        private void TryPulse(SurvivalPhase phase, double dt)
        {
            PulseTimer -= dt;

            if (PulseTimer > 0)
                return;

            PulseTimer = Math.Max(dt, D(phase.PulseInterval));
            double granted = D(phase.BudgetPerPulse) * BudgetMultiplier;
            Budget += granted;
            BudgetGranted += granted;

            double coopPressure = CoopPressure;
            int maxAlive = Math.Max(1, (int)Math.Round(phase.MaxAliveEnemies * coopPressure));
            int maxPurchases = Math.Max(0, (int)Math.Round(assets.Director.MaxPurchasesPerPulse * coopPressure));
            double target = D(phase.TargetPressure) * coopPressure;
            int purchases = 0;

            while (purchases < maxPurchases)
            {
                if (target - Pressure <= 0)
                    break;

                if (TrySelectSpawn(phase, maxAlive, out SpawnCandidate candidate) == false)
                {
                    FailedPurchases++;
                    break;
                }

                // GroupSpawnerUtility found no valid anchor: the front is marked exhausted and, with
                // a single cohesive front, the rest of this pulse's purchases are lost (no budget spent).
                if (rng.Chance(scenario.SpawnFailureChance))
                {
                    FailedPurchases++;
                    break;
                }

                if (candidate.Group != null)
                    SpawnGroup(candidate.Group);
                else
                    Spawn(candidate.Enemy, candidate.Enemy);

                Budget -= candidate.Cost;
                BudgetSpent += candidate.Cost;
                purchases++;
            }
        }

        private struct SpawnCandidate
        {
            public EnemyGroupConfig Group;
            public EnemyDataAsset Enemy;
            public double Cost;
            public double Weight;
        }

        private bool TrySelectSpawn(SurvivalPhase phase, int maxAlive, out SpawnCandidate chosen)
        {
            var valid = new List<SpawnCandidate>();
            double totalWeight = 0;
            int aliveCount = Alive.Count;

            if (phase.AllowedGroups != null)
            {
                foreach (AssetRef<EnemyGroupConfig> groupRef in phase.AllowedGroups)
                {
                    EnemyGroupConfig group = assets.Resolve(groupRef);

                    if (group == null || D(group.Weight) <= 0)
                        continue;
                    if (SurvivalTime < D(group.MinimumSurvivalTime))
                        continue;
                    if (D(group.MaximumSurvivalTime) > 0 && SurvivalTime > D(group.MaximumSurvivalTime))
                        continue;

                    double cost = assets.GroupCost(group);
                    if (cost > Budget)
                        continue;
                    if (aliveCount + group.ComputeMemberCount() > maxAlive)
                        continue;
                    if (group.MaxConcurrent > 0 && AliveFor(group) >= group.MaxConcurrent)
                        continue;

                    double weight = D(group.Weight);
                    valid.Add(new SpawnCandidate { Group = group, Cost = cost, Weight = weight });
                    totalWeight += weight;
                }
            }

            if (phase.AllowedEnemies != null)
            {
                foreach (EnemySpawnEntry entry in phase.AllowedEnemies)
                {
                    EnemyDataAsset data = assets.Resolve(entry.EnemyData);

                    if (data == null || D(entry.Weight) <= 0)
                        continue;
                    if (SurvivalTime < D(entry.MinimumSurvivalTime))
                        continue;
                    if (D(entry.MaximumSurvivalTime) > 0 && SurvivalTime > D(entry.MaximumSurvivalTime))
                        continue;

                    double cost = assets.EnemyCost(data);
                    if (cost > Budget)
                        continue;
                    if (aliveCount + 1 > maxAlive)
                        continue;
                    if (entry.MaxConcurrent > 0 && AliveFor(data) >= entry.MaxConcurrent)
                        continue;

                    double weight = D(entry.Weight);
                    valid.Add(new SpawnCandidate { Enemy = data, Cost = cost, Weight = weight });
                    totalWeight += weight;
                }
            }

            if (valid.Count == 0)
            {
                chosen = default;
                return false;
            }

            double roll = rng.Next01() * totalWeight;
            double cumulative = 0;
            chosen = valid[valid.Count - 1];

            foreach (SpawnCandidate candidate in valid)
            {
                cumulative += candidate.Weight;
                if (roll < cumulative)
                {
                    chosen = candidate;
                    break;
                }
            }

            return true;
        }

        private int AliveFor(AssetObject source) => aliveBySource.TryGetValue(source, out int count) ? count : 0;

        private void SpawnGroup(EnemyGroupConfig group)
        {
            if (group.Members == null)
                return;

            foreach (GroupMemberEntry member in group.Members)
            {
                EnemyDataAsset data = assets.Resolve(member.EnemyData);
                if (data == null)
                    continue;

                for (int i = 0; i < member.Quantity; i++)
                    Spawn(data, group);
            }
        }

        // Mirrors EnemyBalanceUtility.ResolveEnemyStats (+ tier shield via EnemySystem.SeedShield).
        private void Spawn(EnemyDataAsset data, AssetObject source)
        {
            TierStats tier = assets.Tier(data.Tier);
            double hp = Math.Round(D(tier.MaxHealth) * EnemyHpMultiplier(data.Tier)) * D(data.Stats.HealthMultiplier)
                        + D(tier.Shield) * D(data.Stats.ShieldMultiplier);

            var enemy = new SimEnemy
            {
                Data = data,
                Tier = data.Tier,
                Hp = hp,
                MaxHp = hp,
                Cost = assets.EnemyCost(data),
                Exp = D(tier.ExpValue),
                CoinValue = D(tier.CoinValue),
                CoinChance = D(tier.CoinDropChance),
                Source = source,
                Leaker = data.Tier < EnemyTier.Elite && rng.Chance(scenario.EnemyLeakFraction),
            };

            if (enemy.Leaker)
                enemy.RetireAt = RunTime + D(assets.Lifecycle.RecentCombatWindow) + D(assets.Lifecycle.RetireDelay);

            // Spawns land on a ring around the party and walk in at their own MoveSpeed; until they
            // arrive they hold pressure but can't be shot.
            double ring = (D(assets.Director.SpawnRingRadiusMin) + D(assets.Director.SpawnRingRadiusMax)) * 0.5 * scenario.EngageDistanceFraction;
            double speed = D(data.Stats.MoveSpeed);
            double walkIn = speed > 0.1 ? ring / speed : scenario.StationaryEngageSeconds;
            enemy.EngageAt = RunTime + walkIn + scenario.EngageReactionSeconds;

            Alive.Add(enemy);
            aliveBySource[source] = AliveFor(source) + 1;
            SpawnedTotal++;
        }

        public void Remove(SimEnemy enemy)
        {
            Alive.Remove(enemy);
            aliveBySource[enemy.Source] = Math.Max(0, AliveFor(enemy.Source) - 1);
        }

        // Mirrors CombatDirectorUtility.RetireEnemy for spawns the player never reached.
        private void RetireLeakers()
        {
            for (int i = Alive.Count - 1; i >= 0; i--)
            {
                SimEnemy enemy = Alive[i];
                if (enemy.Leaker == false || enemy.RetireAt > RunTime)
                    continue;

                Budget += enemy.Cost * D(assets.Lifecycle.RefundFraction);
                Remove(enemy);
                RetiredTotal++;
            }
        }
    }
}
