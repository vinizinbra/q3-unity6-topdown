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

            for (int seed = 0; seed < seeds; seed++)
            {
                SimPlayer[] players = RunSeed(party, seed, out List<MinuteRow>[] rows);
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
                });
            }

            return results;
        }

        private SimPlayer[] RunSeed(List<CharacterData> party, int seed, out List<MinuteRow>[] rowsPerPlayer)
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
            }).ToArray();

            var rows = new List<MinuteRow>[playerCount];
            for (int i = 0; i < playerCount; i++)
                rows[i] = new List<MinuteRow>();
            rowsPerPlayer = rows;

            double totalXp = 0;
            int displayLevel = 1;
            double coopXp = D(assets.Balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, playerCount));
            double partyKills = 0;
            double dt = Math.Clamp(scenario.TickSeconds, 0.05f, 5f);
            double duration = Math.Max(1, scenario.DurationMinutes) * 60.0;
            double nextMinute = 60;
            int minute = 0;
            double minuteKills = 0, minuteSpawnStart = 0, minuteIdle = 0, minuteTicks = 0;
            double bossHp = 0;
            double coopCoinGain = D(assets.Balance.GetCoopGlobal(CoopGlobalKey.CoinGain, playerCount));
            double barrelCoinsPerSecond = assets.BarrelsPerRun * scenario.BarrelBreakFraction * assets.CoinsPerBarrel / duration;
            double barrelCoins = 0;

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
                    row[Col.OrbPickup] = PickupEfficiency();
                    rows[i].Add(row);
                }

                minuteKills = 0;
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
                // Orbs that expire before anyone reaches them (OrbLifetime) are lost - a per-orb roll
                // against the survival-minute pickup efficiency curve.
                if (rng.Chance(PickupEfficiency()) == false)
                    return;

                SimPlayer finder = players[rng.Next(playerCount)];
                totalXp += enemy.Exp * finder.Stats.ExperienceGainMultiplier;

                if (rng.Chance(enemy.CoinChance))
                {
                    foreach (SimPlayer p in players)
                    {
                        double coins = enemy.CoinValue * coopCoinGain * p.Stats.CoinGainMultiplier;
                        p.Coins += coins;
                        p.CoinsEarned += coins;
                    }
                }

                while (displayLevel < assets.Experience.MaxLevel && totalXp >= RequiredXp(displayLevel + 1))
                {
                    displayLevel++;
                    foreach (SimPlayer p in players)
                        policy.OnLevelUp(p, displayLevel - 1, director.SurvivalTime);
                }
            }

            Snapshot(0);

            // Rows are keyed on SurvivalTime (frozen during Breathing/uncleared Elite). Once the duration
            // is reached the run still plays out a trailing Breathing Break (SurvivalTime doesn't move,
            // but the Boss phase only begins after it); the RunTime cap guards against a config that
            // never lets the clock advance.
            while ((director.SurvivalTime < duration || director.Phase.Kind == SurvivalPhaseKind.Breathing) && director.RunTime < duration * 4)
            {
                director.Tick(dt, out bool enteredBreak);

                if (director.BossReached)
                {
                    bossHp = director.EffectiveHp(EnemyTier.Boss);
                    break;
                }

                if (enteredBreak)
                {
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

                bool breathing = director.Phase.Kind == SurvivalPhaseKind.Breathing;
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

                    for (int i = 0; i < playerCount; i++)
                    {
                        SimPlayer p = players[i];
                        double dps = p.WeaponDps(scenario) + p.SkillDps(scenario) * (p.Skill.UsesArea ? areaFill : 1);
                        shares[i] = dps;
                        budget += dps * dt;
                    }

                    foreach (SimEnemy target in targets)
                    {
                        if (budget <= 0)
                            break;

                        if (target.Hp <= budget)
                        {
                            budget -= target.Hp;
                            budget *= 1 - scenario.OverkillWaste;
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

            return players;
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
