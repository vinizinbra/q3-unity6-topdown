namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    public enum CurveChannel { EnemyHp, EnemyDmg, DirectorBudget, ExpectedPlayerDps }

    public enum CoopGlobalKey { EnemyDamage, DirectorBudget, EliteFrequency, XpRequirement }

    // One row per anchor minute - every channel's value at that point in the run sits together in
    // one place, instead of four separate parallel FP[7] arrays where reading "what's EnemyDmg at
    // 6 minutes" meant counting index positions across unrelated fields.
    [Serializable]
    public class RunCurveAnchor
    {
        public int Minute;
        public FP EnemyHp = FP._1;
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
    // multipliers applied on top of it. Consumed today: EnemyHp/EnemyDmg curves + the EnemyDamage
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
            new() { Minute = 0, EnemyHp = FP._1, EnemyDmg = FP._1, DirectorBudget = FP._1, ExpectedPlayerDps = FP._1 },
            new() { Minute = 2, EnemyHp = FP.FromString("1.6"), EnemyDmg = FP.FromString("1.1"), DirectorBudget = FP.FromString("1.8"), ExpectedPlayerDps = 2 },
            new() { Minute = 4, EnemyHp = FP.FromString("2.5"), EnemyDmg = FP.FromString("1.2"), DirectorBudget = FP.FromString("2.8"), ExpectedPlayerDps = FP.FromString("3.5") },
            new() { Minute = 6, EnemyHp = FP.FromString("3.6"), EnemyDmg = FP.FromString("1.35"), DirectorBudget = 4, ExpectedPlayerDps = FP.FromString("5.5") },
            new() { Minute = 8, EnemyHp = 5, EnemyDmg = FP.FromString("1.5"), DirectorBudget = FP.FromString("5.5"), ExpectedPlayerDps = 9 },
            new() { Minute = 10, EnemyHp = FP.FromString("6.5"), EnemyDmg = FP.FromString("1.6"), DirectorBudget = 7, ExpectedPlayerDps = FP.FromString("13.5") },
            new() { Minute = 12, EnemyHp = FP.FromString("6.5"), EnemyDmg = FP.FromString("1.6"), DirectorBudget = 7, ExpectedPlayerDps = FP.FromString("13.5") },
        };

        [Header("Co-op Scaling - Global")]
        public CoopGlobalRow[] CoopGlobal =
        {
            new() { Key = CoopGlobalKey.EnemyDamage, P1 = FP._1, P2 = FP._1, P3 = FP.FromString("1.05"), P4 = FP.FromString("1.10") },
            new() { Key = CoopGlobalKey.DirectorBudget, P1 = FP._1, P2 = FP.FromString("1.70"), P3 = FP.FromString("2.40"), P4 = 3 },
            new() { Key = CoopGlobalKey.EliteFrequency, P1 = FP._1, P2 = FP.FromString("1.60"), P3 = FP.FromString("2.20"), P4 = FP.FromString("2.80") }, // reserved, no consumer yet
            new() { Key = CoopGlobalKey.XpRequirement, P1 = FP._1, P2 = FP.FromString("1.60"), P3 = FP.FromString("2.20"), P4 = FP.FromString("2.80") },
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
            CurveChannel.EnemyDmg => anchor.EnemyDmg,
            CurveChannel.DirectorBudget => anchor.DirectorBudget,
            CurveChannel.ExpectedPlayerDps => anchor.ExpectedPlayerDps,
            _ => anchor.EnemyHp,
        };

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
