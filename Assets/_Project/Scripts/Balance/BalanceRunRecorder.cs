using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NaughtyAttributes;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

namespace QuantumUser.View.Balance
{
    // Samples the live Frame once per minute of SurvivalTime (Breathing Breaks don't advance it)
    // into Library/BalanceSim/actual_<date>.csv, in the same column layout the Balance
    // Simulator window exports, so a played run can be diffed against its prediction
    // (docs/balance-simulator.md). Drop it on any GameObject in the game scene.
    public unsafe class BalanceRunRecorder : MonoBehaviour
    {
        private const string Tag = "BalanceRecorder";

        // Must match BalanceSimReport.Col (Assets/_QuantumUser/Editor/BalanceSimulator) in order.
        private static readonly string[] Columns =
        {
            "Minute", "SurvivalTime", "Level", "Xp", "PartyKills", "MyKills",
            "KillsFiller", "KillsNormal", "KillsSpecialist", "KillsHeavy", "KillsElite",
            "SpawnedMin", "Alive", "Budget",
            "WeaponDps", "SkillDps", "TotalDps", "ExpectedDps", "DpsRatio",
            "WeaponLevel", "Perks", "CoinsEarned", "CoinsSpent", "Coins",
            "HpNormal", "HpHeavy", "HpElite", "TtkNormal", "TtkHeavy", "TtkElite",
            "KillsPerMin", "SpawnsPerMin", "PressureFill", "IdlePct", "BossHp", "BossTtk",
            "BreakCoins", "BreakLoopCost", "BreakAfford", "LoopCostToDate", "LoopAfford", "OrbPickup",
        };

        private static readonly Dictionary<string, int> ColumnIndex = BuildIndex();

        [SerializeField] private bool autoStart = true;
        [SerializeField] private float sampleIntervalSeconds = 60f;
        [Tooltip("Player DPS the BalanceConfig.ExpectedPlayerDps curve is relative to - keep equal to the scenario's ExpectedDpsBaseline.")]
        [SerializeField] private float expectedDpsBaseline = 50f;

        private class PlayerTrack
        {
            public EntityRef Entity;
            public string Hero;
            public double Kills;
            public double[] KillsByTier = new double[6];
            public double DamageThisSample;
            public double CoinsEarned;
            public double CoinsSpent;
            public double LastCoins;
            public bool CoinsSeen;
        }

        private readonly Dictionary<EntityRef, PlayerTrack> players = new();
        private readonly List<string> lines = new();
        private bool recording;
        private bool sampledStart;
        private double activeTime;
        private double nextSample;
        private int minute;
        private double partyKills;
        private double sampleKills;
        private double sampleXpOrbsCollected;
        private double spawnedThisSample;
        private double idleFrames;
        private double activeFrames;
        private int previousAlive;
        private string filePath;

        private static Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>();
            for (int i = 0; i < Columns.Length; i++)
                index[Columns[i]] = i;
            return index;
        }

        private void Start()
        {
            if (autoStart)
                StartRecording();
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        [Button]
        public void StartRecording()
        {
            if (recording)
                return;

            recording = true;
            players.Clear();
            lines.Clear();
            activeTime = 0;
            nextSample = sampleIntervalSeconds;
            minute = 0;
            partyKills = 0;
            sampleKills = 0;
            sampleXpOrbsCollected = 0;
            spawnedThisSample = 0;
            idleFrames = 0;
            activeFrames = 0;
            previousAlive = 0;
            sampledStart = false;

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BalanceSim"));
            Directory.CreateDirectory(folder);
            filePath = Path.Combine(folder, $"actual_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            QuantumCallback.Subscribe(this, (CallbackSimulateFinished c) => OnSimulateFinished(c.Frame));
            QuantumEvent.Subscribe<EventEntityDied>(this, OnEntityDied);
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventExpOrbCollected>(this, _ => sampleXpOrbsCollected++);
            LogHelper.Log(Tag, $"Recording run to {filePath}");
        }

        [Button]
        public void StopRecording()
        {
            if (recording == false)
                return;

            recording = false;
            QuantumCallback.UnsubscribeListener(this);
            QuantumEvent.UnsubscribeListener(this);
            Flush();
            LogHelper.Log(Tag, $"Stopped - {lines.Count} sample(s) written to {filePath}");
        }

        private void OnSimulateFinished(Frame f)
        {
            if (f == null || f.Global->CurrentState == GameState.Lobby || f.Global->LevelUpScreenOpen)
                return;

            TrackPlayers(f);

            int alive = f.ComponentCount<Enemy>();
            spawnedThisSample += Math.Max(0, alive - previousAlive);
            previousAlive = alive;

            bool breathing = f.Global->CurrentState == GameState.Breathing;
            if (breathing == false)
            {
                activeFrames++;
                if (alive == 0)
                    idleFrames++;
            }

            if (sampledStart == false)
            {
                sampledStart = true;
                Sample(f);
            }

            // Samples are keyed on SurvivalTime (frozen during Breathing), matching the simulator's rows.
            activeTime = f.Global->SurvivalTime.AsDouble;

            if (activeTime + 1e-6 >= nextSample)
            {
                minute++;
                nextSample += sampleIntervalSeconds;
                Sample(f);
            }
        }

        private void TrackPlayers(Frame f)
        {
            var filter = f.Filter<PlayerLink>();

            while (filter.Next(out EntityRef entity, out PlayerLink _))
            {
                if (players.ContainsKey(entity))
                    continue;

                string hero = "Player";
                if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) && stats->CharacterData.IsValid)
                {
                    CharacterData data = f.FindAsset(stats->CharacterData);
                    if (data != null)
                        hero = data.name.Replace("CharacterData", "");
                }

                int duplicates = 0;
                foreach (PlayerTrack other in players.Values)
                    if (other.Hero == hero || other.Hero.StartsWith(hero + " #"))
                        duplicates++;

                players[entity] = new PlayerTrack { Entity = entity, Hero = duplicates == 0 ? hero : $"{hero} #{duplicates + 1}" };
            }
        }

        private void OnEntityDied(EventEntityDied e)
        {
            if (players.ContainsKey(e.Target))
                return;

            partyKills++;
            sampleKills++;

            Frame f = e.Game.Frames.Verified;

            if (players.TryGetValue(ResolveOwner(f, e.Owner), out PlayerTrack track) == false)
                return;

            track.Kills++;

            if (f != null && f.Exists(e.Target) && f.Unsafe.TryGetPointer<Enemy>(e.Target, out var enemy))
            {
                EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
                if (data != null)
                    track.KillsByTier[(int)data.Tier]++;
            }
        }

        private void OnEntityDamaged(EventEntityDamaged e)
        {
            if (e.Silent || players.ContainsKey(e.Target))
                return;

            if (players.TryGetValue(ResolveOwner(e.Game.Frames.Verified, e.Owner), out PlayerTrack track))
                track.DamageThisSample += e.Damage.AsDouble;
        }

        // Skill-spawned entities (sentries, vortices, areas) report themselves as the damage owner;
        // AreaOwner (SpawnedEntitySpawner) points back at the hero that deployed them.
        private EntityRef ResolveOwner(Frame f, EntityRef owner)
        {
            if (players.ContainsKey(owner) || f == null || owner == EntityRef.None || f.Exists(owner) == false)
                return owner;

            return f.TryGet(owner, out AreaOwner areaOwner) ? areaOwner.Owner : owner;
        }

        private void Sample(Frame f)
        {
            string phase = "";
            SurvivalConfig survival = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (survival != null && survival.Phases != null && survival.Phases.Length > 0)
                phase = survival.Phases[Mathf.Clamp(f.Global->CurrentPhaseIndex, 0, survival.Phases.Length - 1)].Name ?? "";

            BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);
            EnemyTierStatsConfig tiers = f.FindAsset(f.RuntimeConfig.EnemyTierStatsConfig);
            FP survivalTime = f.Global->SurvivalTime;
            int playerCount = Math.Max(1, f.PlayerConnectedCount);
            double expected = balance != null ? balance.Evaluate(CurveChannel.ExpectedPlayerDps, survivalTime).AsDouble * expectedDpsBaseline : 0;
            double hpNormal = EffectiveHp(balance, tiers, EnemyTier.Normal, survivalTime, playerCount);
            double hpHeavy = EffectiveHp(balance, tiers, EnemyTier.Heavy, survivalTime, playerCount);
            double hpElite = EffectiveHp(balance, tiers, EnemyTier.Elite, survivalTime, playerCount);
            double interval = Math.Max(1, sampleIntervalSeconds);
            double partyDps = 0;
            foreach (PlayerTrack t in players.Values)
                partyDps += t.DamageThisSample / interval;

            double alive = f.ComponentCount<Enemy>();
            double pressure = 0;
            var enemies = f.Filter<Enemy>();
            while (enemies.Next(out EntityRef _, out Enemy enemy))
            {
                EnemyDataAsset data = f.FindAsset(enemy.EnemyData);
                if (data != null && tiers != null)
                    pressure += data.ResolveCost(f).AsDouble;
            }

            double targetPressure = survival != null && survival.Phases.Length > 0
                ? survival.Phases[Mathf.Clamp(f.Global->CurrentPhaseIndex, 0, survival.Phases.Length - 1)].TargetPressure.AsDouble
                : 0;

            foreach (PlayerTrack track in players.Values)
            {
                var row = new double[Columns.Length];
                string weaponName = "-";
                double weaponLevel = 0, perks = 0, weaponDps = 0, coins = 0;

                if (f.Unsafe.TryGetPointer<CharacterStats>(track.Entity, out var stats))
                {
                    coins = stats->Coins.AsDouble;
                    if (track.CoinsSeen)
                    {
                        double delta = coins - track.LastCoins;
                        if (delta > 0) track.CoinsEarned += delta; else track.CoinsSpent -= delta;
                    }
                    track.LastCoins = coins;
                    track.CoinsSeen = true;

                    if (f.Unsafe.TryGetPointer<Weapon>(track.Entity, out var weapon))
                    {
                        WeaponDataAsset data = f.FindAsset(weapon->WeaponData);
                        weaponName = data != null ? (string.IsNullOrEmpty(data.DisplayName) ? data.name : data.DisplayName) : "-";
                        weaponLevel = weapon->Level;
                        for (int i = 0; i < weapon->Perks.Length; i++)
                            if (weapon->Perks[i].IsValid)
                                perks++;
                        weaponDps = TheoreticalWeaponDps(data, weapon, stats);
                    }
                }

                double actualDps = track.DamageThisSample / interval;
                Set(row, "Minute", minute);
                Set(row, "SurvivalTime", survivalTime.AsDouble);
                Set(row, "Level", f.Global->Level + 1);
                Set(row, "Xp", f.Global->TotalExperience.AsDouble);
                Set(row, "PartyKills", partyKills);
                Set(row, "MyKills", track.Kills);
                Set(row, "KillsFiller", track.KillsByTier[(int)EnemyTier.Filler]);
                Set(row, "KillsNormal", track.KillsByTier[(int)EnemyTier.Normal]);
                Set(row, "KillsSpecialist", track.KillsByTier[(int)EnemyTier.Specialist]);
                Set(row, "KillsHeavy", track.KillsByTier[(int)EnemyTier.Heavy]);
                Set(row, "KillsElite", track.KillsByTier[(int)EnemyTier.Elite]);
                Set(row, "SpawnedMin", spawnedThisSample);
                Set(row, "Alive", alive);
                Set(row, "Budget", f.Global->DirectorBudget.AsDouble);
                Set(row, "WeaponDps", weaponDps);
                Set(row, "TotalDps", actualDps);
                Set(row, "ExpectedDps", expected);
                Set(row, "DpsRatio", expected > 0 ? actualDps / expected : 0);
                Set(row, "WeaponLevel", weaponLevel);
                Set(row, "Perks", perks);
                Set(row, "CoinsEarned", track.CoinsEarned);
                Set(row, "CoinsSpent", track.CoinsSpent);
                Set(row, "Coins", coins);
                Set(row, "HpNormal", hpNormal);
                Set(row, "HpHeavy", hpHeavy);
                Set(row, "HpElite", hpElite);
                Set(row, "TtkNormal", partyDps > 0 ? hpNormal / partyDps : 0);
                Set(row, "TtkHeavy", partyDps > 0 ? hpHeavy / partyDps : 0);
                Set(row, "TtkElite", partyDps > 0 ? hpElite / partyDps : 0);
                Set(row, "KillsPerMin", sampleKills);
                Set(row, "SpawnsPerMin", spawnedThisSample);
                Set(row, "PressureFill", targetPressure > 0 ? pressure / targetPressure : 0);
                Set(row, "IdlePct", activeFrames > 0 ? idleFrames / activeFrames : 0);
                // Every non-boss enemy kill drops one XP orb (ExperienceUtility.TrySpawnDrop), so
                // collected / kills is the share that didn't expire on the ground.
                Set(row, "OrbPickup", sampleKills > 0 ? Math.Min(1, sampleXpOrbsCollected / sampleKills) : 0);

                var sb = new StringBuilder();
                sb.Append(Escape(track.Hero)).Append(',').Append(Escape(phase)).Append(',').Append(Escape(weaponName));
                foreach (double value in row)
                    sb.Append(',').Append(value.ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add(sb.ToString());

                track.DamageThisSample = 0;
            }

            sampleKills = 0;
            sampleXpOrbsCollected = 0;
            spawnedThisSample = 0;
            idleFrames = 0;
            activeFrames = 0;
            Flush();
        }

        // Same formula as BalanceSimulator's SimWeapon.Dps with HitEfficiency 1 - the "if every shot
        // landed" ceiling, to compare against the measured TotalDps.
        private static double TheoreticalWeaponDps(WeaponDataAsset data, Weapon* weapon, CharacterStats* stats)
        {
            if (data == null)
                return 0;

            double fireRate = Math.Max(0.01, data.FireRate.AsDouble);
            double cooldown = 1 / fireRate * weapon->FireCooldownMultiplier.AsDouble / Math.Max(0.01, stats->AttackSpeedMultiplier.AsDouble);
            double magazine = Math.Max(1, weapon->MagazineSize);
            double reload = weapon->ReloadDuration.AsDouble / Math.Max(0.01, stats->ReloadSpeedMultiplier.AsDouble);
            double shotsPerSecond = magazine / (magazine * cooldown + reload);
            double critChance = Math.Clamp(stats->CriticalChance.AsDouble + weapon->CriticalChance.AsDouble, 0, 1);
            double critMultiplier = stats->CriticalDamageMultiplier.AsDouble + weapon->CriticalDamageBonus.AsDouble;
            double hit = Math.Max(1, data.PelletCount) * data.Damage.AsDouble * weapon->DamageMultiplier.AsDouble
                         * stats->DamageMultiplier.AsDouble * stats->WeaponDamageMultiplier.AsDouble * (1 + critChance * (critMultiplier - 1));
            return hit * shotsPerSecond;
        }

        private static double EffectiveHp(BalanceConfig balance, EnemyTierStatsConfig tiers, EnemyTier tier, FP survivalTime, int playerCount)
        {
            if (balance == null || tiers == null)
                return 0;

            // Tier shield is per-enemy (Stats.ShieldMultiplier, 0 on every authored enemy) - report HP only, same as the simulator.
            return Math.Round(tiers.Get(tier).MaxHealth.AsDouble * balance.EvaluateEnemyHp(tier, survivalTime).AsDouble * balance.GetCoopHp(tier, playerCount).AsDouble);
        }

        private static void Set(double[] row, string column, double value)
        {
            if (ColumnIndex.TryGetValue(column, out int index))
                row[index] = value;
        }

        private static string Escape(string value) => value != null && value.Contains(',') ? $"\"{value.Replace("\"", "\"\"")}\"" : value ?? "";

        private void Flush()
        {
            if (string.IsNullOrEmpty(filePath) || lines.Count == 0)
                return;

            try
            {
                File.WriteAllText(filePath, "Hero,Phase,Weapon," + string.Join(",", Columns) + "\n" + string.Join("\n", lines) + "\n");
            }
            catch (Exception e)
            {
                LogHelper.Error(Tag, $"Could not write {filePath}: {e.Message}");
            }
        }
    }
}
