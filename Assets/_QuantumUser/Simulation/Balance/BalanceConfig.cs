namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;
    using UnityEngine.Serialization;

    // EnemyHp is split in two so the swarm (Filler/Normal) and the threats (Specialist/Heavy/
    // Elite/Boss) can ramp on independent curves - BalanceConfig.GetEnemyHpChannel maps a tier to
    // its channel, EvaluateEnemyHp is the one-call form every HP consumer should use.
    public enum CurveChannel { EnemyHpLight, EnemyHpHeavy, EnemyDmg, DirectorBudget, ExpectedPlayerDps }

    public enum CoopGlobalKey { EnemyDamage, DirectorBudget, EliteFrequency, XpRequirement, DirectorPressure, CoinGain }

    // One row per anchor minute - every channel's value at that point in the run sits together in
    // one place, instead of four separate parallel FP[7] arrays where reading "what's EnemyDmg at
    // 6 minutes" meant counting index positions across unrelated fields.
    [Serializable]
    public class RunCurveAnchor
    {
        public int Minute;
        [FormerlySerializedAs("EnemyHp")]
        public FP EnemyHpLight = FP._1; // Filler / Normal
        public FP EnemyHpHeavy = FP._1; // Specialist / Heavy / Elite / Boss
        public FP EnemyDmg = FP._1;
        public FP DirectorBudget = FP._1;    // consumed by CombatDirectorUtility.ResolveBudgetMultiplier
        public FP ExpectedPlayerDps = FP._1; // reserved for Survival Director Milestone 7 - no consumer yet
    }

    [Serializable]
    public class CoopGlobalRow
    {
        public CoopGlobalKey Key;
        public FP P1 = FP._1;
        public FP P2 = FP._1;
        public FP P3 = FP._1;
        public FP P4 = FP._1;
    }

    [Serializable]
    public class CoopHpRow
    {
        public EnemyTier Tier;
        public FP P1 = FP._1;
        public FP P2 = FP._1;
        public FP P3 = FP._1;
        public FP P4 = FP._1;
    }

    // Single consolidated balance asset: time-based run curves and player-count co-op scaling
    // (global multipliers + per-tier HP multipliers) - one AssetRef on RuntimeConfig instead of
    // one-SO-per-table. See docs/run-curves-coop-scaling.md. The per-tier HP baseline itself is
    // NOT duplicated here - EnemyBalanceUtility.ResolveEnemyStats reads it straight from the
    // pre-existing EnemyTierStatsConfig.MaxHealth, this asset only supplies the curve/co-op
    // multipliers applied on top of it. Consumed today: EnemyHpLight/EnemyHpHeavy/EnemyDmg curves + the EnemyDamage
    // co-op row + CoopHp (EnemyBalanceUtility.ResolveEnemyStats); the DirectorBudget curve + co-op
    // row (CombatDirectorUtility.ResolveBudgetMultiplier - docs/survival-director.md's
    // "Milestone 7"); and the XpRequirement co-op row (ExperienceUtility.
    // ResolveXpRequirementMultiplier). ExpectedPlayerDps/EliteFrequency remain reserved, no
    // consumer yet.
    public class BalanceConfig : AssetObject
    {
        [Header("Run Curves (one row per anchor minute)")]
        public RunCurveAnchor[] Curves =
        {
            new() { Minute = 0, EnemyHpLight = FP._1, EnemyHpHeavy = FP._1, EnemyDmg = FP._1, DirectorBudget = FP._1, ExpectedPlayerDps = FP._1 },
            new() { Minute = 2, EnemyHpLight = FP.FromString("1.6"), EnemyHpHeavy = FP.FromString("1.6"), EnemyDmg = FP.FromString("1.1"), DirectorBudget = FP.FromString("1.8"), ExpectedPlayerDps = 2 },
            new() { Minute = 4, EnemyHpLight = FP.FromString("2.5"), EnemyHpHeavy = FP.FromString("2.5"), EnemyDmg = FP.FromString("1.2"), DirectorBudget = FP.FromString("2.8"), ExpectedPlayerDps = FP.FromString("3.5") },
            new() { Minute = 6, EnemyHpLight = FP.FromString("3.6"), EnemyHpHeavy = FP.FromString("3.6"), EnemyDmg = FP.FromString("1.35"), DirectorBudget = 4, ExpectedPlayerDps = FP.FromString("5.5") },
            new() { Minute = 8, EnemyHpLight = 5, EnemyHpHeavy = 5, EnemyDmg = FP.FromString("1.5"), DirectorBudget = FP.FromString("5.5"), ExpectedPlayerDps = 9 },
            new() { Minute = 10, EnemyHpLight = FP.FromString("6.5"), EnemyHpHeavy = FP.FromString("6.5"), EnemyDmg = FP.FromString("1.6"), DirectorBudget = 7, ExpectedPlayerDps = FP.FromString("13.5") },
            new() { Minute = 12, EnemyHpLight = FP.FromString("6.5"), EnemyHpHeavy = FP.FromString("6.5"), EnemyDmg = FP.FromString("1.6"), DirectorBudget = 7, ExpectedPlayerDps = FP.FromString("13.5") },
        };

        [Header("Co-op Scaling - Global")]
        public CoopGlobalRow[] CoopGlobal =
        {
            new() { Key = CoopGlobalKey.EnemyDamage, P1 = FP._1, P2 = FP._1, P3 = FP.FromString("1.05"), P4 = FP.FromString("1.10") },
            new() { Key = CoopGlobalKey.DirectorBudget, P1 = FP._1, P2 = FP.FromString("1.70"), P3 = FP.FromString("2.40"), P4 = 3 },
            new() { Key = CoopGlobalKey.EliteFrequency, P1 = FP._1, P2 = FP.FromString("1.60"), P3 = FP.FromString("2.20"), P4 = FP.FromString("2.80") }, // reserved, no consumer yet
            new() { Key = CoopGlobalKey.XpRequirement, P1 = FP._1, P2 = FP.FromString("1.60"), P3 = FP.FromString("2.20"), P4 = FP.FromString("2.80") },
            // Scales the Director's hard caps (SurvivalPhase.TargetPressure / MaxAliveEnemies and
            // DirectorConfig.MaxPurchasesPerPulse) with player count. Without it a cohesive party
            // keeps solo caps: the extra DirectorBudget income can't be spent, spawn (and therefore
            // XP) throughput stays flat while XpRequirement climbs - co-op levelled slower than solo.
            // Consumed by PlayerClusterDirectorUtility.BuildAnchors / CombatDirectorUtility.TryPulse.
            new() { Key = CoopGlobalKey.DirectorPressure, P1 = FP._1, P2 = FP.FromString("1.70"), P3 = FP.FromString("2.40"), P4 = 3 },
            // Every collected coin orb credits EVERY wallet (CurrencyOrbSystem.Grant ->
            // CoinUtility.GrantAll) while kills scale with the party, so per-wallet kill income would
            // rise with player count and co-op could afford the whole Break loop solo can't. Applied to
            // enemy kill drops only (CoinUtility.TrySpawnDrop) - barrel loot doesn't grow with the party
            // - so each party size lands near the solo wallet. Sized for kills scaling ~x1.5/x2.0/x2.4.
            new() { Key = CoopGlobalKey.CoinGain, P1 = FP._1, P2 = FP.FromString("0.65"), P3 = FP.FromString("0.50"), P4 = FP.FromString("0.42") },
        };

        [Header("Co-op Scaling - Enemy HP (per Tier)")]
        public CoopHpRow[] CoopHp =
        {
            new() { Tier = EnemyTier.Filler, P1 = FP._1, P2 = FP.FromString("1.15"), P3 = FP.FromString("1.25"), P4 = FP.FromString("1.35") },
            new() { Tier = EnemyTier.Normal, P1 = FP._1, P2 = FP.FromString("1.15"), P3 = FP.FromString("1.25"), P4 = FP.FromString("1.35") },
            new() { Tier = EnemyTier.Specialist, P1 = FP._1, P2 = FP.FromString("1.15"), P3 = FP.FromString("1.25"), P4 = FP.FromString("1.35") },
            new() { Tier = EnemyTier.Heavy, P1 = FP._1, P2 = FP.FromString("1.35"), P3 = FP.FromString("1.60"), P4 = FP.FromString("1.85") },
            new() { Tier = EnemyTier.Elite, P1 = FP._1, P2 = FP.FromString("1.50"), P3 = FP.FromString("1.90"), P4 = FP.FromString("2.30") },
            new() { Tier = EnemyTier.Boss, P1 = FP._1, P2 = FP.FromString("1.70"), P3 = FP.FromString("2.20"), P4 = FP.FromString("2.60") },
        };

        private static FP GetChannelValue(RunCurveAnchor anchor, CurveChannel channel) => channel switch
        {
            CurveChannel.EnemyHpHeavy => anchor.EnemyHpHeavy,
            CurveChannel.EnemyDmg => anchor.EnemyDmg,
            CurveChannel.DirectorBudget => anchor.DirectorBudget,
            CurveChannel.ExpectedPlayerDps => anchor.ExpectedPlayerDps,
            _ => anchor.EnemyHpLight,
        };

        // Filler/Normal ride the Light curve; everything from Specialist up rides the Heavy one.
        public static CurveChannel GetEnemyHpChannel(EnemyTier tier) => tier switch
        {
            EnemyTier.Filler => CurveChannel.EnemyHpLight,
            EnemyTier.Normal => CurveChannel.EnemyHpLight,
            _ => CurveChannel.EnemyHpHeavy,
        };

        // Run-curve HP multiplier for a tier - the single entry point for every HP consumer
        // (EnemyBalanceUtility, BalanceSimDirector, BalanceRunRecorder) so the tier->curve mapping
        // lives in exactly one place.
        public FP EvaluateEnemyHp(EnemyTier tier, FP elapsedSeconds)
            => Evaluate(GetEnemyHpChannel(tier), elapsedSeconds);

        // Clamped flat below the first anchor (minute 0) and above the last (minute 12).
        public FP Evaluate(CurveChannel channel, FP elapsedSeconds)
        {
            RunCurveAnchor first = Curves[0];
            if (elapsedSeconds <= FP._0)
                return GetChannelValue(first, channel);

            RunCurveAnchor last = Curves[Curves.Length - 1];
            FP lastSeconds = last.Minute * 60;
            if (elapsedSeconds >= lastSeconds)
                return GetChannelValue(last, channel);

            for (int i = 0; i < Curves.Length - 1; i++)
            {
                RunCurveAnchor from = Curves[i];
                RunCurveAnchor to = Curves[i + 1];
                FP toSeconds = to.Minute * 60;

                if (elapsedSeconds <= toSeconds)
                {
                    FP fromSeconds = from.Minute * 60;
                    FP t = (elapsedSeconds - fromSeconds) / (toSeconds - fromSeconds);
                    return FPMath.Lerp(GetChannelValue(from, channel), GetChannelValue(to, channel), t);
                }
            }

            return GetChannelValue(last, channel);
        }

        // playerCount clamped to [1,4] rather than erroring - keeps this safe against any future
        // room-size change.
        public FP GetCoopGlobal(CoopGlobalKey key, int playerCount)
        {
            int clamped = playerCount < 1 ? 1 : (playerCount > 4 ? 4 : playerCount);

            foreach (CoopGlobalRow row in CoopGlobal)
            {
                if (row.Key != key)
                    continue;

                return clamped switch { 1 => row.P1, 2 => row.P2, 3 => row.P3, _ => row.P4 };
            }

            Log.Error($"[Balance] CoopGlobalRow for key {key} not found on BalanceConfig - returning 1 (no-op)");
            return FP._1;
        }

        // Array + linear search matched by Tier field - never (int)tier indexing. playerCount
        // clamped to [1,4].
        public FP GetCoopHp(EnemyTier tier, int playerCount)
        {
            int clamped = playerCount < 1 ? 1 : (playerCount > 4 ? 4 : playerCount);

            foreach (CoopHpRow row in CoopHp)
            {
                if (row.Tier != tier)
                    continue;

                return clamped switch { 1 => row.P1, 2 => row.P2, 3 => row.P3, _ => row.P4 };
            }

            Log.Error($"[Balance] CoopHpRow for tier {tier} not found on BalanceConfig - returning 1 (no-op)");
            return FP._1;
        }
    }
}
