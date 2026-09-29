namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    // Every per-tier lever DifficultyConfig carries - one field each on DifficultyRow, one
    // NightmareGrowthRow each, and one field each on the DifficultySnapshot global (Difficulty.qtn).
    public enum DifficultyChannel
    {
        EnemyHpLight,
        EnemyHpHeavy,
        EnemyDamage,
        SpawnDensity,
        EliteWeight,
        AnticipationSpeed,
        RecoverySpeed,
        EnemyProjectileSpeed,
        EnemyProjectileLead,
        EnemyMoveSpeed,
    }

    // One named tier's multipliers. Medium is the pre-difficulty tuning, so every field defaults to 1.
    [Serializable]
    public class DifficultyRow
    {
        public FP EnemyHpLight = FP._1;         // Filler / Normal
        public FP EnemyHpHeavy = FP._1;         // Specialist / Heavy / Elite / Boss
        public FP EnemyDamage = FP._1;
        public FP SpawnDensity = FP._1;         // Director budget + MaxAlive + TargetPressure together
        public FP EliteWeight = FP._1;          // Elite+ group/enemy roll weight
        public FP AnticipationSpeed = FP._1;    // >1 = shorter Preparation/Telegraph windup
        public FP RecoverySpeed = FP._1;        // >1 = shorter post-attack recovery
        public FP EnemyProjectileSpeed = FP._1;
        public FP EnemyProjectileLead = FP._1;  // scales delivery LeadFactor/MaxLeadDistance
        public FP EnemyMoveSpeed = FP._1;

        public FP Get(DifficultyChannel channel) => channel switch
        {
            DifficultyChannel.EnemyHpLight => EnemyHpLight,
            DifficultyChannel.EnemyHpHeavy => EnemyHpHeavy,
            DifficultyChannel.EnemyDamage => EnemyDamage,
            DifficultyChannel.SpawnDensity => SpawnDensity,
            DifficultyChannel.EliteWeight => EliteWeight,
            DifficultyChannel.AnticipationSpeed => AnticipationSpeed,
            DifficultyChannel.RecoverySpeed => RecoverySpeed,
            DifficultyChannel.EnemyProjectileSpeed => EnemyProjectileSpeed,
            DifficultyChannel.EnemyProjectileLead => EnemyProjectileLead,
            _ => EnemyMoveSpeed,
        };
    }

    // Nightmare N = Hard x PerLevel^N, clamped to Cap (<= 0 = uncapped, still bounded by
    // DifficultyConfig.MaxMultiplier so FP / Health never overflow at absurd levels).
    [Serializable]
    public class NightmareGrowthRow
    {
        public DifficultyChannel Channel;
        public FP PerLevel = FP._1;
        public FP Cap;
    }

    // Easy/Medium/Hard are authored rows; Nightmare is endless, derived from Hard by per-channel
    // compound growth - so Nightmare 1 is always strictly harder than Hard. Resolved once per match
    // into Global.Difficulty by DifficultySystem - see docs/difficulty.md.
    public class DifficultyConfig : AssetObject
    {
        [Header("Named Tiers")]
        public DifficultyRow Easy = new()
        {
            EnemyHpLight = FP.FromString("0.7"),
            EnemyHpHeavy = FP.FromString("0.7"),
            EnemyDamage = FP.FromString("0.6"),
            SpawnDensity = FP.FromString("0.8"),
            EliteWeight = FP.FromString("0.7"),
            AnticipationSpeed = FP.FromString("0.8"),
            RecoverySpeed = FP.FromString("0.85"),
            EnemyProjectileSpeed = FP.FromString("0.85"),
            EnemyProjectileLead = FP.FromString("0.5"),
            EnemyMoveSpeed = FP.FromString("0.95"),
        };

        public DifficultyRow Medium = new();

        public DifficultyRow Hard = new()
        {
            EnemyHpLight = FP.FromString("1.35"),
            EnemyHpHeavy = FP.FromString("1.45"),
            EnemyDamage = FP.FromString("1.3"),
            SpawnDensity = FP.FromString("1.15"),
            EliteWeight = FP.FromString("1.3"),
            AnticipationSpeed = FP.FromString("1.15"),
            RecoverySpeed = FP.FromString("1.1"),
            EnemyProjectileSpeed = FP.FromString("1.15"),
            EnemyProjectileLead = FP.FromString("1.2"),
            EnemyMoveSpeed = FP.FromString("1.05"),
        };

        [Header("Nightmare (Hard x PerLevel^N, clamped to Cap)")]
        public NightmareGrowthRow[] NightmareGrowth =
        {
            new() { Channel = DifficultyChannel.EnemyHpLight, PerLevel = FP.FromString("1.2") },
            new() { Channel = DifficultyChannel.EnemyHpHeavy, PerLevel = FP.FromString("1.25") },
            new() { Channel = DifficultyChannel.EnemyDamage, PerLevel = FP.FromString("1.15") },
            new() { Channel = DifficultyChannel.SpawnDensity, PerLevel = FP.FromString("1.06"), Cap = FP.FromString("1.6") },
            new() { Channel = DifficultyChannel.EliteWeight, PerLevel = FP.FromString("1.15"), Cap = 3 },
            new() { Channel = DifficultyChannel.AnticipationSpeed, PerLevel = FP.FromString("1.05"), Cap = FP.FromString("1.5") },
            new() { Channel = DifficultyChannel.RecoverySpeed, PerLevel = FP.FromString("1.05"), Cap = FP.FromString("1.4") },
            new() { Channel = DifficultyChannel.EnemyProjectileSpeed, PerLevel = FP.FromString("1.05"), Cap = FP.FromString("1.5") },
            new() { Channel = DifficultyChannel.EnemyProjectileLead, PerLevel = FP.FromString("1.05"), Cap = FP.FromString("1.6") },
            new() { Channel = DifficultyChannel.EnemyMoveSpeed, PerLevel = FP.FromString("1.03"), Cap = FP.FromString("1.25") },
        };

        // Hard ceiling on any resolved multiplier, capped channel or not - keeps boss MaxHealth
        // (base x run curve x co-op x this) well inside Int32/FP range however high NightmareLevel goes.
        public FP MaxMultiplier = 10000;

        public FP Resolve(DifficultyTier tier, int nightmareLevel, DifficultyChannel channel)
        {
            switch (tier)
            {
                case DifficultyTier.Easy: return Easy.Get(channel);
                case DifficultyTier.Medium: return Medium.Get(channel);
                case DifficultyTier.Hard: return Hard.Get(channel);
            }

            FP value = Hard.Get(channel);
            NightmareGrowthRow growth = FindGrowth(channel);

            if (growth == null || growth.PerLevel <= FP._0)
                return value;

            FP ceiling = growth.Cap > FP._0 ? FPMath.Min(growth.Cap, MaxMultiplier) : MaxMultiplier;
            int levels = nightmareLevel < 1 ? 1 : nightmareLevel;

            // Iterative compound (deterministic, no float pow), early-out once the ceiling is reached.
            for (int i = 0; i < levels && value < ceiling; i++)
                value *= growth.PerLevel;

            return FPMath.Min(value, ceiling);
        }

        public DifficultySnapshot BuildSnapshot(DifficultyTier tier, int nightmareLevel)
        {
            int level = tier == DifficultyTier.Nightmare ? (nightmareLevel < 1 ? 1 : nightmareLevel) : 0;

            return new DifficultySnapshot
            {
                Tier = tier,
                NightmareLevel = level,
                EnemyHpLight = Resolve(tier, level, DifficultyChannel.EnemyHpLight),
                EnemyHpHeavy = Resolve(tier, level, DifficultyChannel.EnemyHpHeavy),
                EnemyDamage = Resolve(tier, level, DifficultyChannel.EnemyDamage),
                SpawnDensity = Resolve(tier, level, DifficultyChannel.SpawnDensity),
                EliteWeight = Resolve(tier, level, DifficultyChannel.EliteWeight),
                AnticipationSpeed = Resolve(tier, level, DifficultyChannel.AnticipationSpeed),
                RecoverySpeed = Resolve(tier, level, DifficultyChannel.RecoverySpeed),
                EnemyProjectileSpeed = Resolve(tier, level, DifficultyChannel.EnemyProjectileSpeed),
                EnemyProjectileLead = Resolve(tier, level, DifficultyChannel.EnemyProjectileLead),
                EnemyMoveSpeed = Resolve(tier, level, DifficultyChannel.EnemyMoveSpeed),
            };
        }

        // "Nightmare 5", "Hard", ... - for HUD/loading/debug readouts.
        public static string GetDisplayName(DifficultyTier tier, int nightmareLevel)
            => tier == DifficultyTier.Nightmare ? $"Nightmare {(nightmareLevel < 1 ? 1 : nightmareLevel)}" : tier.ToString();

        private NightmareGrowthRow FindGrowth(DifficultyChannel channel)
        {
            if (NightmareGrowth == null)
                return null;

            foreach (NightmareGrowthRow row in NightmareGrowth)
            {
                if (row.Channel == channel)
                    return row;
            }

            return null;
        }
    }
}
