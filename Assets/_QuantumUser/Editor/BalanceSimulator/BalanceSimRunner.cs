namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using static BalanceSimAssets;

    // Drives one full run: Director ticks, party damage, kills -> XP/coins, level-up picks, Break
    // shopping, and per-minute snapshots. Monte-Carlo averaged over scenario.Seeds.
    public class BalanceSimRunner
    {
        private readonly BalanceSimAssets assets;
        private readonly BalanceSimScenario scenario;

        public BalanceSimRunner(BalanceSimAssets assets, BalanceSimScenario scenario)
        {
            this.assets = assets;
            this.scenario = scenario;
        }

        public List<HeroResult> RunAll(Action<float> progress = null)
        {
            var results = new List<HeroResult>();

            if (scenario.Party != null && scenario.Party.Count > 0)
            {
                List<CharacterData> party = scenario.Party.Where(h => h != null).ToList();
                List<HeroResult> coop = RunParty(party, party.Count, true, progress);
                foreach (HeroResult r in coop)
                    r.PlayerCount = party.Count;
                results.AddRange(coop);
                return results;
            }

            int[] playerCounts = scenario.SimulateAllPlayerCounts ? new[] { 1, 2, 3, 4 } : new[] { Math.Clamp(scenario.PlayerCount, 1, 4) };
            int totalRuns = playerCounts.Length * assets.AllHeroes.Count;
            int done = 0;

            foreach (int playerCount in playerCounts)
            {
                foreach (CharacterData hero in assets.AllHeroes)
                {
                    List<CharacterData> party = Enumerable.Repeat(hero, playerCount).ToList();
                    int runIndex = done;
                    List<HeroResult> solo = RunParty(party, 1, false, p => progress?.Invoke((runIndex + p) / totalRuns));
                    foreach (HeroResult r in solo)
                        r.PlayerCount = playerCount;
                    results.AddRange(solo);
                    done++;
                }
            }

            return results;
        }

        private List<HeroResult> RunParty(List<CharacterData> party, int reportedPlayers, bool coop, Action<float> progress)
        {
            int seeds = Math.Max(1, scenario.Seeds);
            var perSeed = new List<List<MinuteRow>>[party.Count];
            for (int i = 0; i < party.Count; i++)
                perSeed[i] = new List<List<MinuteRow>>();

            SimPlayer[] firstSeedPlayers = null;
            var totals = new RunTotals();

            for (int seed = 0; seed < seeds; seed++)
            {
                SimPlayer[] players = RunSeed(party, seed, out List<MinuteRow>[] rows, out RunTotals seedTotals);
                totals.TotalXp += seedTotals.TotalXp / seeds;
                totals.FinalLevel += seedTotals.FinalLevel / seeds;
                totals.ShardsDropped += seedTotals.ShardsDropped / seeds;
                totals.RiftShards += seedTotals.RiftShards / seeds;
                totals.BossShards += seedTotals.BossShards / seeds;
                firstSeedPlayers ??= players;
                for (int i = 0; i < party.Count; i++)
                    perSeed[i].Add(rows[i]);

                progress?.Invoke((seed + 1f) / seeds);
            }

            var results = new List<HeroResult>();

            for (int i = 0; i < reportedPlayers; i++)
            {
                string name = party[i].name.Replace("CharacterData", "");
                if (coop && party.Count(h => h == party[i]) > 1)
                    name += $" #{i + 1}";

                results.Add(new HeroResult
                {
                    HeroName = name,
                    SkillModel = firstSeedPlayers[i].Skill.Model,
                    Rows = BalanceSimReport.Average(perSeed[i]),
                    PickLog = firstSeedPlayers[i].PickLog,
                    Totals = totals,
                });
            }

            return results;
        }

        private SimPlayer[] RunSeed(List<CharacterData> party, int seed, out List<MinuteRow>[] rowsPerPlayer, out RunTotals totals)
        {
            var rng = new SimRng((scenario.BaseSeed + seed) * 7919 + 17);
            int playerCount = party.Count;
            var director = new BalanceSimDirector(assets, scenario, playerCount, rng);
            var policy = new BalanceSimPolicy(assets, scenario, rng);
            double levelBonus = D(assets.LevelUp.WeaponLevelDamageBonusPerLevel);

            SimPlayer[] players = party.Select(hero => new SimPlayer
            {
                Data = hero,
                HeroName = hero.name,
                Stats = SimStats.From(hero),
                Weapon = SimWeapon.Create(assets.Resolve(hero.StartingWeapon), 0, levelBonus, assets),
                Skill = BalanceSimSkillModel.Evaluate(hero, assets, scenario),
                DamageBonusPerLevel = D(assets.Experience.DamageBonusPerLevel),
                TickSeconds = assets.SimTickSeconds,
                DashCooldown = assets.Resolve(hero.DashSkill) is SkillData dash ? Math.Max(0.1, D(dash.Cooldown)) : 4,
                DashUseEfficiency = scenario.DashUseEfficiency,
            }).ToArray();

            var rows = new List<MinuteRow>[playerCount];
            for (int i = 0; i < playerCount; i++)
                rows[i] = new List<MinuteRow>();
            rowsPerPlayer = rows;

            double totalXp = 0;
            int displayLevel = 1;
            double coopXp = D(assets.Balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, playerCount)) * assets.WorldXpRequirement;
            double partyKills = 0;
            double dt = Math.Clamp(scenario.TickSeconds, 0.05f, 5f);
            double duration = Math.Max(1, scenario.DurationMinutes) * 60.0;
            double nextMinute = 60;
            int minute = 0;
            double minuteKills = 0, minuteSpawnStart = 0, minuteIdle = 0, minuteTicks = 0;
            double bossHp = 0;
            double coopCoinGain = D(assets.Balance.GetCoopGlobal(CoopGlobalKey.CoinGain, playerCount)) * assets.WorldCoinGain;
            double barrelCoinsPerSecond = assets.BarrelsPerRun * scenario.BarrelBreakFraction * assets.CoinsPerBarrel / duration;
            double barrelCoins = 0;

            // Orbs nobody walked over yet. They expire after their OrbLifetime (RunTime - DestroyAfterTime
            // keeps ticking through the SurvivalTime freezes), unless a Breathing Break is secured first:
            // CurrencyOrbVacuumUtility then collects every orb still on the map.
            double xpOrbLifetime = assets.Experience != null ? D(assets.Experience.OrbLifetime) : 60;
            double coinOrbLifetime = assets.Coin != null ? D(assets.Coin.OrbLifetime) : 30;
            var pendingXp = new List<(double ExpireAt, double Exp)>();
            var pendingCoins = new List<(double ExpireAt, double Value)>();
            var pendingShards = new List<(double ExpireAt, double Value)>();
            double shardOrbLifetime = assets.RiftShard != null ? D(assets.RiftShard.OrbLifetime) : 30;
            double shardsDropped = 0, shardsCollected = 0;
            bool wasSecured = false;
            var killsAtSnapshot = new double[playerCount];
            double minuteXpOrbs = 0;

            // Solo loss from the curve, shrunk per extra player (more bodies covering the ground).
            double PickupEfficiency()
            {
                if (scenario.OrbPickupEfficiency == null || scenario.OrbPickupEfficiency.length == 0)
                    return 1;
                double solo = Math.Clamp(scenario.OrbPickupEfficiency.Evaluate((float)(director.SurvivalTime / 60.0)), 0, 1);
                double loss = (1 - solo) * Math.Pow(1 - Math.Clamp(scenario.CoopPickupBonus, 0, 1), playerCount - 1);
                return Math.Clamp(1 - loss, 0, 1);
            }

            double RequiredXp(int level) => D(assets.Experience.RequiredExperience.Evaluate((FP)level)) * D(assets.Experience.DifficultyMultiplier) * coopXp;

            void Snapshot(int m)
            {
                double partyDps = players.Sum(p => p.TotalDps(scenario));
                double expected = director.ExpectedPlayerDpsCurve * scenario.ExpectedDpsBaseline;
                double hpNormal = director.EffectiveHp(EnemyTier.Normal);
                double hpHeavy = director.EffectiveHp(EnemyTier.Heavy);
                double hpElite = director.EffectiveHp(EnemyTier.Elite);
                double spawnedThisMinute = director.SpawnedTotal - minuteSpawnStart;
                double idlePct = minuteTicks > 0 ? minuteIdle / minuteTicks : 0;

                for (int i = 0; i < playerCount; i++)
                {
                    // Kills/s over the minute that just ended - feeds Uncontrolled Fury's extension.
                    if (m > 0)
                        players[i].KillRate = (players[i].Kills - killsAtSnapshot[i]) / 60.0;
                    killsAtSnapshot[i] = players[i].Kills;

                    SimPlayer p = players[i];
                    double dps = p.TotalDps(scenario);
                    var row = new MinuteRow { Phase = director.PhaseName, Weapon = p.Weapon.Name };
                    row[Col.Minute] = m;
                    row[Col.SurvivalTime] = director.SurvivalTime;
                    row[Col.Level] = displayLevel;
                    row[Col.Xp] = totalXp;
                    row[Col.PartyKills] = partyKills;
                    row[Col.MyKills] = p.Kills;
                    row[Col.KillsFiller] = p.KillsByTier[(int)EnemyTier.Filler];
                    row[Col.KillsNormal] = p.KillsByTier[(int)EnemyTier.Normal];
                    row[Col.KillsSpecialist] = p.KillsByTier[(int)EnemyTier.Specialist];
                    row[Col.KillsHeavy] = p.KillsByTier[(int)EnemyTier.Heavy];
                    row[Col.KillsElite] = p.KillsByTier[(int)EnemyTier.Elite];
                    row[Col.SpawnedMin] = spawnedThisMinute;
                    row[Col.Alive] = director.Alive.Count;
                    row[Col.Budget] = director.Budget;
                    row[Col.WeaponDps] = p.WeaponDps(scenario);
                    row[Col.SkillDps] = p.SkillDps(scenario);
                    row[Col.TotalDps] = dps;
                    row[Col.ExpectedDps] = expected;
                    row[Col.DpsRatio] = expected > 0 ? dps / expected : 0;
                    row[Col.WeaponLevel] = p.Weapon.Level;
                    row[Col.Perks] = p.Weapon.Perks.Count;
                    row[Col.CoinsEarned] = p.CoinsEarned;
                    row[Col.CoinsSpent] = p.CoinsSpent;
                    row[Col.Coins] = p.Coins;
                    row[Col.HpNormal] = hpNormal;
                    row[Col.HpHeavy] = hpHeavy;
                    row[Col.HpElite] = hpElite;
                    row[Col.TtkNormal] = partyDps > 0 ? hpNormal / partyDps : 0;
                    row[Col.TtkHeavy] = partyDps > 0 ? hpHeavy / partyDps : 0;
                    row[Col.TtkElite] = partyDps > 0 ? hpElite / partyDps : 0;
                    row[Col.KillsPerMin] = minuteKills;
                    row[Col.SpawnsPerMin] = spawnedThisMinute;
                    row[Col.PressureFill] = director.TargetPressure > 0 ? director.Pressure / director.TargetPressure : 0;
                    row[Col.IdlePct] = idlePct;
                    row[Col.BossHp] = bossHp;
                    row[Col.BossTtk] = bossHp > 0 && partyDps > 0 ? bossHp / partyDps : 0;

                    if (p.HasPendingBreak)
                    {
                        row[Col.BreakCoins] = p.PendingBreakCoins;
                        row[Col.BreakLoopCost] = p.PendingBreakLoopCost;
                        row[Col.BreakAfford] = p.PendingBreakLoopCost > 0 ? p.PendingBreakCoins / p.PendingBreakLoopCost : 0;
                        p.HasPendingBreak = false;
                    }

                    row[Col.LoopCostToDate] = p.LoopCostToDate;
                    row[Col.LoopAfford] = p.LoopCostToDate > 0 ? p.CoinsEarned / p.LoopCostToDate : 0;
                    // Collected so far (every wallet gets each shard), before the run-end floor/Boss shard.
                    row[Col.RiftShards] = shardsCollected * D(p.Data.RiftShardGainMultiplier);
                    row[Col.OrbPickup] = minuteKills > 0 ? Math.Min(1, minuteXpOrbs / minuteKills) : 0;
                    row[Col.Players] = playerCount;
                    rows[i].Add(row);
                }

                minuteKills = 0;
                minuteXpOrbs = 0;
                minuteIdle = 0;
                minuteTicks = 0;
                minuteSpawnStart = director.SpawnedTotal;
            }

            void OnKill(SimEnemy enemy, int killer)
            {
                director.Remove(enemy);
                partyKills++;
                minuteKills++;
                players[killer].Kills++;
                players[killer].KillsByTier[(int)enemy.Tier]++;

                // XP orbs credit the shared total scaled by the finder's own multiplier; coin orbs
                // credit EVERY wallet (CoinUtility.GrantAll), each scaled by that player's own multiplier.
                // A per-orb roll against the survival-minute pickup efficiency curve; a missed orb waits
                // on the ground until it expires (OrbLifetime) or a secured Break sweeps it in.
                double pickup = PickupEfficiency();

                // Elite chests (EnemyChestDropUtility.TrySpawnDrop): an extra forced-category pick
                // for every player (BeginChestScreen). Chests don't expire, so no pickup roll.
                LevelUpCategory? chestKind = assets.ResolveChestKind(enemy.Data);
                if (chestKind.HasValue && rng.Chance(D(enemy.Data.ChestDropChance)))
                {
                    foreach (SimPlayer p in players)
                        policy.OnChest(p, chestKind.Value, director.SurvivalTime);
                }

                // Coin orbs are separate from the XP orb (own lifetime, CoinConfig.OrbLifetime).
                if (rng.Chance(enemy.CoinChance))
                {
                    if (rng.Chance(pickup * Math.Clamp(scenario.CoinPickupFactor, 0, 1)))
                        GrantCoins(enemy.CoinValue);
                    else
                        pendingCoins.Add((director.RunTime + coinOrbLifetime, enemy.CoinValue));
                }

                // Rift Shards (RiftShardUtility.TrySpawnDrop): per-tier value/chance, a 30s orb like a
                // coin - XP pickup share x ShardPickupFactor (ranged Specialists die far away), same Break sweep.
                if (enemy.ShardValue > 0 && rng.Chance(enemy.ShardChance))
                {
                    shardsDropped += enemy.ShardValue;
                    if (rng.Chance(pickup * Math.Clamp(scenario.ShardPickupFactor, 0, 1)))
                        shardsCollected += enemy.ShardValue;
                    else
                        pendingShards.Add((director.RunTime + shardOrbLifetime, enemy.ShardValue));
                }

                if (rng.Chance(pickup))
                    GrantXp(enemy.Exp);
                else
                    pendingXp.Add((director.RunTime + xpOrbLifetime, enemy.Exp));
            }

            void GrantCoins(double value)
            {
                foreach (SimPlayer p in players)
                {
                    double coins = value * coopCoinGain * p.Stats.CoinGainMultiplier;
                    p.Coins += coins;
                    p.CoinsEarned += coins;
                }
            }

            void GrantXp(double exp)
            {
                minuteXpOrbs++;
                SimPlayer finder = players[rng.Next(playerCount)];
                totalXp += exp * finder.Stats.ExperienceGainMultiplier;

                while (displayLevel < assets.Experience.MaxLevel && totalXp >= RequiredXp(displayLevel + 1))
                {
                    displayLevel++;
                    foreach (SimPlayer p in players)
                    {
                        p.DisplayLevel = displayLevel;
                        // LevelUpUtility.OpenUpgradeScreen rolls for Global.Level + 1 after the
                        // increment - the new displayed level (first level-up = 2 -> LevelSequence[1]).
                        policy.OnLevelUp(p, displayLevel, director.SurvivalTime);
                    }
                }
            }

            Snapshot(0);

            // Rows are keyed on SurvivalTime (frozen during Breathing/uncleared Elite). Once the duration
            // is reached the run still plays out a trailing Breathing Break (SurvivalTime doesn't move,
            // but the Boss phase only begins after it); the RunTime cap guards against a config that
            // never lets the clock advance.
            while ((director.SurvivalTime < duration || director.Phase.Kind == SurvivalPhaseKind.Breathing) && director.RunTime < duration * 4)
            {
                director.Tick(dt, out bool _);

                if (director.BossReached)
                {
                    bossHp = director.EffectiveHp(EnemyTier.Boss);
                    break;
                }

                bool breathing = director.Phase.Kind == SurvivalPhaseKind.Breathing;

                // SurvivalProgressionUtility: on the tick a Break is secured (Breathing + nothing alive)
                // CurrencyOrbVacuumUtility sweeps in every orb that hasn't expired yet.
                bool secured = breathing && director.Alive.Count == 0;
                if (secured && wasSecured == false)
                {
                    foreach (var orb in pendingCoins)
                        if (orb.ExpireAt > director.RunTime)
                            GrantCoins(orb.Value);
                    foreach (var orb in pendingXp)
                        if (orb.ExpireAt > director.RunTime)
                            GrantXp(orb.Exp);
                    foreach (var orb in pendingShards)
                        if (orb.ExpireAt > director.RunTime)
                            shardsCollected += orb.Value;
                    pendingShards.Clear();
                    pendingCoins.Clear();
                    pendingXp.Clear();

                    // The Break's POIs only open once it's secured (GameState.Breathing needs
                    // BreathingAreaSecured), so the shopping runs here - with the swept coins in hand.
                    double loopCost = policy.ExpectedLoopCost(director.SurvivalTime, director.BreathingIndex);

                    foreach (SimPlayer p in players)
                    {
                        p.HasPendingBreak = true;
                        p.PendingBreakCoins = p.Coins;
                        p.PendingBreakLoopCost = loopCost;
                        p.LoopCostToDate += loopCost;
                        policy.OnBreathingBreak(p, director.SurvivalTime, director.BreathingIndex);
                    }
                }

                wasSecured = secured;
                pendingCoins.RemoveAll(o => o.ExpireAt <= director.RunTime);
                pendingXp.RemoveAll(o => o.ExpireAt <= director.RunTime);
                pendingShards.RemoveAll(o => o.ExpireAt <= director.RunTime);

                if (breathing == false)
                {
                    minuteTicks++;

                    // Barrels are broken while moving through the level - spread their loot evenly;
                    // their coin orbs credit every wallet too.
                    if (barrelCoinsPerSecond > 0)
                    {
                        foreach (SimPlayer p in players)
                        {
                            double coins = barrelCoinsPerSecond * dt * p.Stats.CoinGainMultiplier;
                            p.Coins += coins;
                            p.CoinsEarned += coins;
                        }
                        barrelCoins += barrelCoinsPerSecond * dt;
                    }
                }

                List<SimEnemy> targets = director.Alive.Where(e => e.EngageAt <= director.RunTime).OrderBy(e => e.Hp).ToList();

                if (targets.Count == 0)
                {
                    if (breathing == false)
                        minuteIdle++;
                }
                else
                {
                    double areaFill = Math.Min(1, targets.Count / Math.Max(1, scenario.AreaTargets));
                    var shares = new double[playerCount];
                    double budget = 0;
                    double partyDps = 0;

                    for (int i = 0; i < playerCount; i++)
                    {
                        SimPlayer p = players[i];
                        double dps = p.WeaponDpsWithBuffs(scenario) + p.OwnSkillDps(scenario) * (p.Skill.UsesArea ? areaFill : 1);
                        shares[i] = dps;
                        partyDps += dps;
                        budget += dps * dt;
                    }

                    foreach (SimEnemy target in targets)
                    {
                        if (budget <= 0)
                            break;

                        if (target.Hp <= budget)
                        {
                            budget -= target.Hp;
                            budget -= scenario.RetargetSeconds * partyDps / playerCount;
                            OnKill(target, PickByShare(rng, shares));
                        }
                        else
                        {
                            target.Hp -= budget;
                            budget = 0;
                        }
                    }
                }

                if (director.SurvivalTime + 1e-6 >= nextMinute)
                {
                    minute++;
                    Snapshot(minute);
                    nextMinute += 60;
                }
            }

            // Boss numbers go on the last survival minute's row - the Boss phase itself adds no time.
            if (director.BossReached)
            {
                double partyDps = players.Sum(p => p.TotalDps(scenario));
                for (int i = 0; i < playerCount; i++)
                {
                    if (rows[i].Count == 0)
                        continue;

                    MinuteRow last = rows[i][rows[i].Count - 1];
                    last[Col.BossHp] = bossHp;
                    last[Col.BossTtk] = partyDps > 0 ? bossHp / partyDps : 0;
                }
            }

            // The Boss fight isn't simulated: assume a Victory. The defeat cinematic's burst (BossDefeatSystem) grants
            // BossDataAsset.DefeatRiftShardReward plus every shard orb still lying on the map, and the Boss's own tier
            // drop lands in time to be hoovered up with them.
            // Banked = floor(RiftShardsEarned) of the local player (RunResultManager.BankRiftShards).
            double bossShards = 0;
            if (director.BossReached)
            {
                TierStats bossTier = assets.Tier(EnemyTier.Boss);
                bossShards = D(bossTier.RiftShardValue) * Math.Clamp(D(bossTier.RiftShardDropChance), 0, 1);
                bossShards += ResolveBossDefeatReward(assets);

                foreach (var orb in pendingShards)
                    if (orb.ExpireAt > director.RunTime)
                        bossShards += orb.Value;
            }

            double shardGain = D(party[0].RiftShardGainMultiplier);
            totals = new RunTotals
            {
                TotalXp = totalXp,
                FinalLevel = displayLevel,
                ShardsDropped = shardsDropped + bossShards,
                RiftShards = Math.Floor((shardsCollected + bossShards) * shardGain),
                BossShards = bossShards,
            };

            return players;
        }

        // DefeatRiftShardReward of the boss the LAST Boss phase spawns (the one whose death is Victory): phase ->
        // BossPrototype -> its Enemy component's EnemyData -> BossDataAsset.
        private static double ResolveBossDefeatReward(BalanceSimAssets assets)
        {
            SurvivalPhase[] phases = assets.Survival != null ? assets.Survival.Phases : null;

            for (int i = (phases?.Length ?? 0) - 1; i >= 0; i--)
            {
                if (phases[i].Kind != SurvivalPhaseKind.Boss || phases[i].BossPrototype.IsValid == false)
                    continue;

                EntityPrototype prototype = assets.Resolve(phases[i].BossPrototype);
                if (prototype == null || prototype.Container.Components == null)
                    return 0;

                foreach (ComponentPrototype component in prototype.Container.Components)
                {
                    if (component is Quantum.Prototypes.EnemyPrototype enemy && assets.Resolve(enemy.EnemyData) is BossDataAsset boss)
                        return D(boss.DefeatRiftShardReward);
                }

                return 0;
            }

            return 0;
        }

        private static int PickByShare(SimRng rng, double[] shares)
        {
            double total = shares.Sum();
            if (total <= 0)
                return 0;

            double roll = rng.Next01() * total;
            double cumulative = 0;

            for (int i = 0; i < shares.Length; i++)
            {
                cumulative += shares[i];
                if (roll < cumulative)
                    return i;
            }

            return shares.Length - 1;
        }
    }
}
