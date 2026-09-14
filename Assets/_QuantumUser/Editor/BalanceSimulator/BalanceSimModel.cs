namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using Quantum;
    using static BalanceSimAssets;

    public class SimRng
    {
        private readonly Random random;
        public SimRng(int seed) { random = new Random(seed); }
        public double Next01() => random.NextDouble();
        public int Next(int maxExclusive) => random.Next(maxExclusive);
        public bool Chance(double chance) => chance > 0 && random.NextDouble() < chance;
    }

    // The CharacterStats fields the DPS math reads - seeded from CharacterData, mutated by Global
    // Upgrades exactly the way CharacterStatMultiplierUpgradeData.Apply does (multiply in place).
    public class SimStats
    {
        public double DamageMultiplier = 1;
        public double WeaponDamageMultiplier = 1;
        public double SkillDamageMultiplier = 1;
        public double CriticalChance;
        public double CriticalDamageMultiplier = 1.5;
        public double AttackSpeedMultiplier = 1;
        public double ReloadSpeedMultiplier = 1;
        public double SkillCooldownMultiplier = 1;
        public double SkillDurationMultiplier = 1;
        public double AreaRadiusMultiplier = 1;
        public double ExperienceGainMultiplier = 1;
        public double CoinGainMultiplier = 1;
        public double MaxHealthMultiplier = 1;
        public double DamageTakenMultiplier = 1;
        public double MagazineMultiplier = 1;
        public double MaxHealth;
        public double MaxShield;

        public static SimStats From(CharacterData data) => new()
        {
            DamageMultiplier = D(data.DamageMultiplier),
            WeaponDamageMultiplier = D(data.WeaponDamageMultiplier),
            SkillDamageMultiplier = D(data.SkillDamageMultiplier),
            CriticalChance = D(data.CriticalChance),
            CriticalDamageMultiplier = D(data.CriticalDamageMultiplier),
            AttackSpeedMultiplier = D(data.AttackSpeedMultiplier),
            ReloadSpeedMultiplier = D(data.ReloadSpeedMultiplier),
            SkillCooldownMultiplier = D(data.SkillCooldownMultiplier),
            SkillDurationMultiplier = D(data.SkillDurationMultiplier),
            AreaRadiusMultiplier = D(data.AreaRadiusMultiplier),
            ExperienceGainMultiplier = D(data.ExperienceGainMultiplier),
            CoinGainMultiplier = D(data.CoinGainMultiplier),
            MaxHealthMultiplier = D(data.MaxHealthMultiplier),
            DamageTakenMultiplier = D(data.DamageTakenMultiplier),
            MaxHealth = D(data.BaseMaxHealth),
            MaxShield = D(data.BaseMaxShield),
        };

        public SimStats Clone() => (SimStats)MemberwiseClone();

        // Returns false when the upgrade has no effect the simulator tracks (move speed, regen...).
        public bool ApplyGlobalUpgrade(GlobalUpgradeData upgrade)
        {
            double m = upgrade is CharacterStatMultiplierUpgradeData stat ? D(stat.Multiplier) : 1;

            switch (upgrade)
            {
                case WeaponDamageUpgradeData: WeaponDamageMultiplier *= m; return true;
                case FireRateUpgradeData: AttackSpeedMultiplier *= m; return true;
                case ReloadSpeedUpgradeData: ReloadSpeedMultiplier *= m; return true;
                case MagazineSizeUpgradeData mag: MagazineMultiplier *= D(mag.Multiplier); return true;
                case CriticalChanceUpgradeData crit: CriticalChance += D(crit.Chance); return true;
                case CriticalDamageUpgradeData: CriticalDamageMultiplier *= m; return true;
                case SkillDamageUpgradeData: SkillDamageMultiplier *= m; return true;
                case SkillCooldownUpgradeData: SkillCooldownMultiplier *= m; return true;
                case SkillDurationUpgradeData: SkillDurationMultiplier *= m; return true;
                case SkillAreaUpgradeData: AreaRadiusMultiplier *= m; return true;
                case ExperienceGainUpgradeData: ExperienceGainMultiplier *= m; return true;
                case CoinGainUpgradeData: CoinGainMultiplier *= m; return true;
                case MaxHealthUpgradeData: MaxHealthMultiplier *= m; return true;
                case ToughnessUpgradeData: DamageTakenMultiplier *= m; return true;
                default: return false;
            }
        }
    }

    public class SimWeapon
    {
        public WeaponDataAsset Data;
        public int Level;
        public List<WeaponPerkData> Perks = new();
        public double DamageMultiplier = 1;
        public double FireCooldownMultiplier = 1;
        public double CriticalChance;
        public double CriticalDamageBonus;
        public double MagazineSize;
        public double ReloadDuration;
        public double UnquantifiedBonus;

        // Quantified WeaponDataAsset.BaseTraits contributions - a weapon's OWN authored
        // WeaponPerkData picks (WeaponSystem.ApplyBaseTraits), resolved in Create below via
        // AddBaseTrait and folded into Dps() the same way a rolled perk of the same class would be.
        // See Quantify's own comment for why these live on SimWeapon rather than being read directly
        // off WeaponDataAsset.
        public int BurstCount = 1;
        public double BurstDelay;
        public bool CritOnFinalBurstShot;
        public double FinalRoundBonus;
        public double RampMaxStacks;
        public double RampFireRateBonusPerStack;
        public double RampDamageBonusPerStack;
        public int SplitShotCount;
        public double SplitShotMultiplier;
        public double CritExplosionRadius;
        public double CritExplosionMultiplier;
        public int ExplosiveSequenceInterval;
        public double ExplosiveSequenceDamageMultiplier;

        public const int MaxPerks = 5;
        public bool HasFreeSlot => Perks.Count < MaxPerks;
        public string Name => Data != null ? (string.IsNullOrEmpty(Data.DisplayName) ? Data.name : Data.DisplayName) : "-";

        // assets resolves WeaponDataAsset.BaseTraits (a weapon's own baseline WeaponPerkData picks -
        // Double Barrel/Burst Rifle's BurstFireWeaponPerkData, Frost Revolver's
        // FinalRoundWeaponPerkData, Drum SMG's SuppressiveCycleWeaponPerkData, Cluster Launcher's
        // SplitShotWeaponPerkData, Hellshot's ExplosiveCritWeaponPerkData, Disruptor's
        // CritStunWeaponPerkData); null (every existing call site that predates BaseTraits) just
        // skips them, same as a weapon authored with an empty list.
        public static SimWeapon Create(WeaponDataAsset data, int level, double levelBonusPerLevel, BalanceSimAssets assets = null)
        {
            if (data == null)
                return new SimWeapon();

            var w = new SimWeapon
            {
                Data = data,
                CriticalChance = D(data.CriticalChance),
                CriticalDamageBonus = D(data.CriticalDamageBonus),
                MagazineSize = data.MagazineSize,
                ReloadDuration = D(data.ReloadDuration),
            };

            for (int i = 0; i < level; i++)
                w.AddLevel(levelBonusPerLevel);

            if (assets != null && data.BaseTraits != null)
            {
                foreach (AssetRef<WeaponPerkData> traitRef in data.BaseTraits)
                {
                    // 0: every BaseTraits perk this sim knows about today is fully quantified by
                    // Quantify below (CritStunWeaponPerkData included - see its own case) - nothing
                    // here has ever hit the unquantified fallback, unlike a rolled Weapon.Perks pick.
                    w.AddBaseTrait(assets.Resolve(traitRef), 0);
                }
            }

            return w;
        }

        public SimWeapon Clone()
        {
            var c = (SimWeapon)MemberwiseClone();
            c.Perks = new List<WeaponPerkData>(Perks);
            return c;
        }

        // Mirrors WeaponSystem.AddLevel / CompoundLevelMultiplier.
        public void AddLevel(double bonusPerLevel)
        {
            Level++;
            DamageMultiplier *= 1 + bonusPerLevel;
        }

        public bool HasPerk(WeaponPerkData perk) => Perks.Contains(perk);

        // Mirrors each WeaponPerkData.Apply the simulator can quantify; anything else is worth a
        // flat UnquantifiedPerkDpsValue. Returns false when the slot cap rejected it.
        public bool AddPerk(WeaponPerkData perk, double unquantifiedValue)
        {
            if (HasFreeSlot == false || perk == null)
                return false;

            Perks.Add(perk);
            Quantify(perk, unquantifiedValue);
            return true;
        }

        // A weapon's OWN authored WeaponDataAsset.BaseTraits (see Create) - same Quantify as a
        // rolled perk, but never slot-capped (BaseTraits don't compete with Weapon.Perks' 5-slot
        // roll) and not recorded into Perks/HasPerk, since a weapon's own baseline isn't a "pick"
        // the pick log or perk-card UI reasoning applies to.
        public void AddBaseTrait(WeaponPerkData trait, double unquantifiedValue)
        {
            if (trait != null)
                Quantify(trait, unquantifiedValue);
        }

        // Shared by AddPerk (rolled, slot-capped) and AddBaseTrait (a weapon's own authored
        // baseline, uncapped) - both draw from the exact same WeaponPerkData hierarchy now (see
        // WeaponSystem.ApplyBaseTraits), so one switch quantifies either source identically.
        private void Quantify(WeaponPerkData perk, double unquantifiedValue)
        {
            switch (perk)
            {
                case DamageMultiplierWeaponPerkData p: DamageMultiplier *= D(p.Multiplier); break;
                case FireRateWeaponPerkData p: FireCooldownMultiplier /= Math.Max(0.01, D(p.Multiplier)); break;
                case CooldownMultiplierWeaponPerkData p: FireCooldownMultiplier *= D(p.Multiplier); break;
                case HeavyCaliberWeaponPerkData p:
                    DamageMultiplier *= D(p.DamageMultiplier);
                    FireCooldownMultiplier /= Math.Max(0.01, D(p.FireRateMultiplier));
                    break;
                case CriticalChanceWeaponPerkData p: CriticalChance += D(p.Chance); break;
                case CriticalDamageWeaponPerkData p: CriticalDamageBonus += D(p.Bonus); break;
                case MagazineMultiplierWeaponPerkData p: MagazineSize = Math.Max(1, Math.Round(MagazineSize * D(p.Multiplier))); break;
                case ReloadSpeedWeaponPerkData p: ReloadDuration /= Math.Max(0.01, D(p.Multiplier)); break;
                case FinalRoundWeaponPerkData p: FinalRoundBonus += D(p.DamageBonus); break;
                case SplitShotWeaponPerkData p:
                    SplitShotCount = Math.Max(SplitShotCount, p.Count);
                    SplitShotMultiplier = Math.Max(SplitShotMultiplier, D(p.DamageMultiplier));
                    break;
                case ExplosiveCritWeaponPerkData p:
                    CritExplosionRadius = Math.Max(CritExplosionRadius, D(p.Radius));
                    CritExplosionMultiplier = Math.Max(CritExplosionMultiplier, D(p.DamageMultiplier));
                    break;
                case RelentlessFireWeaponPerkData p:
                    RampMaxStacks = Math.Max(RampMaxStacks, p.MaxStacks);
                    RampDamageBonusPerStack += D(p.DamageBonusPerStack);
                    break;
                case SuppressiveCycleWeaponPerkData p:
                    RampMaxStacks = Math.Max(RampMaxStacks, p.MaxStacks);
                    RampFireRateBonusPerStack += D(p.FireRateBonusPerStack);
                    break;
                case OverchargeCycleWeaponPerkData p:
                    RampMaxStacks = Math.Max(RampMaxStacks, p.MaxStacks);
                    RampDamageBonusPerStack += D(p.DamageBonusPerStack);
                    RampFireRateBonusPerStack += D(p.FireRateBonusPerStack);
                    break;
                case BurstFireWeaponPerkData p:
                    BurstCount = p.BurstCount;
                    BurstDelay = D(p.BurstDelay);
                    CritOnFinalBurstShot = p.CritOnFinalBurstShot;
                    break;
                case ExplosiveSequenceWeaponPerkData p:
                    ExplosiveSequenceInterval = ExplosiveSequenceInterval <= 0 ? p.Interval : Math.Min(ExplosiveSequenceInterval, p.Interval);
                    ExplosiveSequenceDamageMultiplier = Math.Max(ExplosiveSequenceDamageMultiplier, D(p.DamageMultiplier));
                    break;
                case CritStunWeaponPerkData: break; // crowd control only - no DPS contribution to quantify
                default: UnquantifiedBonus += unquantifiedValue; break;
            }
        }

        // Sustained DPS including reloads - mirrors WeaponSystem.ResolveFireCooldown +
        // StatUtility.GetFireCooldown + DamageUtility.ResolveOutgoingDamage's weapon path, plus the
        // burst-fire/base-trait mechanisms added for Double Barrel/Burst Rifle/Arcshot/Hellshot/
        // Napalm Launcher/Drum SMG/Disruptor/Slugger/Cluster Launcher/Frost Revolver. Ramp
        // (Drum SMG) and Split Shot (Cluster Launcher) use a flat heuristic rather than a real
        // build-up/hit-chance simulation - see their own comments below; treat those two
        // contributions as knobs, not exact numbers, same as every other approximation this sim
        // already documents in docs/balance-simulator.md.
        public double Dps(SimStats stats, double fireRateBonus = 0, double reloadBonus = 0)
        {
            if (Data == null)
                return 0;

            // Ramp (Relentless Fire/Suppressive Cycle/Overcharge Cycle, rolled OR Drum SMG's own
            // baseline SuppressiveCycleWeaponPerkData) - RampStacks climbs toward RampMaxStacks
            // while firing and resets on any pause (WeaponSystem.TickRamp), so a sustained-fire DPS
            // figure can't just use the max stack bonus. Half of max is a decisive, not-fitted
            // placeholder for "mostly ramped up during a sustained burst" - calibrate against a
            // recorded Drum SMG run if this ever matters for real balance work.
            double rampFireRateBonus = RampMaxStacks * RampFireRateBonusPerStack * 0.5;
            double rampDamageFactor = 1 + RampMaxStacks * RampDamageBonusPerStack * 0.5;

            double fireRate = Math.Max(0.01, D(Data.FireRate));
            double cooldown = 1 / fireRate * FireCooldownMultiplier / (1 + fireRateBonus + rampFireRateBonus) / Math.Max(0.01, stats.AttackSpeedMultiplier);

            // Double Barrel/Burst Rifle's own baseline BurstFireWeaponPerkData - BurstCount shots
            // leave BurstDelay apart per trigger pull, and only the LAST one pays the full
            // FireCooldownTimer (WeaponSystem.Update's burst-start block/TickWeaponBurst) - so the
            // average per-shot cadence blends the two instead of treating every shot as its own full
            // cooldown. BurstCount <= 1 (every weapon without that trait) collapses to the plain
            // `cooldown` above, completely unaffected.
            int burstCount = Math.Max(1, BurstCount);
            double effectiveCooldown = burstCount <= 1
                ? cooldown
                : ((burstCount - 1) * BurstDelay + cooldown) / burstCount;

            double magazine = Math.Max(1, Math.Round(MagazineSize * stats.MagazineMultiplier));
            double reload = ReloadDuration / Math.Max(0.01, stats.ReloadSpeedMultiplier) / (1 + reloadBonus);
            double shotsPerSecond = magazine / (magazine * effectiveCooldown + reload);

            double critChance = Math.Clamp(stats.CriticalChance + CriticalChance, 0, 1);
            double critMultiplier = stats.CriticalDamageMultiplier + CriticalDamageBonus;

            // Burst Rifle's 3rd-shot-always-crits - one of every BurstCount shots skips the roll
            // entirely (DamageUtility.ResolveOutgoingDamage's forceCritical), the rest still roll
            // normally, so the average hit multiplier blends a guaranteed crit into the usual
            // probabilistic one instead of just adding to CriticalChance.
            double normalCritFactor = 1 + critChance * (critMultiplier - 1);
            double critFactor = CritOnFinalBurstShot && burstCount > 1
                ? ((burstCount - 1) * normalCritFactor + critMultiplier) / burstCount
                : normalCritFactor;

            // Frost Revolver's own baseline FinalRoundWeaponPerkData - only the last shot of a
            // magazine gets it, so it's worth 1/magazine of its full value on average across a
            // sustained clip.
            double finalRoundFactor = 1 + FinalRoundBonus / magazine;

            // Cluster Launcher's own baseline SplitShotWeaponPerkData - SplitShotCount fragments at
            // SplitShotMultiplier each, assumed to reliably land (same "decisive, not exhaustive"
            // placeholder the ramp bonus above uses).
            double splitShotFactor = 1 + SplitShotCount * SplitShotMultiplier;

            // Hellshot's own baseline ExplosiveCritWeaponPerkData - procs on the same critChance
            // roll as the hit itself (plus the guaranteed final-burst-shot crit above, if any),
            // dealing an extra CritExplosionMultiplier x this hit's own damage.
            double critExplosionFactor = CritExplosionRadius > 0
                ? 1 + critChance * CritExplosionMultiplier
                : 1;

            // Cluster Launcher's own baseline ExplosiveSequenceWeaponPerkData (Interval 1 - every
            // shot detonates) - 1/Interval of shots proc it, each for an extra
            // ExplosiveSequenceDamageMultiplier x that shot's own damage.
            double explosiveSequenceFactor = ExplosiveSequenceInterval > 0
                ? 1 + ExplosiveSequenceDamageMultiplier / ExplosiveSequenceInterval
                : 1;

            double hit = Math.Max(1, Data.PelletCount) * D(Data.Damage) * DamageMultiplier
                         * stats.DamageMultiplier * stats.WeaponDamageMultiplier * critFactor * (1 + UnquantifiedBonus)
                         * finalRoundFactor * splitShotFactor * critExplosionFactor * rampDamageFactor * explosiveSequenceFactor;

            return hit * shotsPerSecond;
        }

        // Bare asset DPS with no character multipliers (used for sentry/skill-mounted weapons).
        // No BalanceSimAssets to resolve BaseTraits with, so a sentry/skill-mounted weapon's own
        // baseline traits (none currently author any) don't factor in here - same reach a rolled
        // Weapon.Perks pick already doesn't have on this path either.
        public static double BaseDps(WeaponDataAsset data) => Create(data, 0, 0).Dps(new SimStats { CriticalDamageMultiplier = 1.5 });
    }

    public class SkillEstimate
    {
        public string Model = "none";
        public double DamagePerActivation;
        public double Cooldown = 1;
        public double ActiveDuration;
        public double WeaponFireRateBonus;
        public double WeaponReloadBonus;
        public bool UsesArea;

        // "Skill Damage" basis every percent-of-basis Ascension scales off (KaiAscensionUtility.
        // ResolveVortexSkillDamage & friends): ProjectileSkillData.Damage / JuggernautSkillData.Damage /
        // SpawnSentrySkillAction.SkillDamage.
        public double Basis;
        public double ImpactDamage;
        public double TickDamage;
        public double TickInterval;
        public double TickWindow;
        public double MountedWeaponDamage;
        public double BuffUptime;
    }

    public struct UpgradeValue
    {
        public double SkillDamage;
        public double WeaponBonus;
        public bool Quantified => SkillDamage > 0 || WeaponBonus > 0;
    }

    public class SimPlayer
    {
        public CharacterData Data;
        public string HeroName;
        public SimStats Stats;
        public SimWeapon Weapon;
        public SkillEstimate Skill;
        public double SkillUpgradeBonus;
        public double SkillUpgradeDamage;
        public double WeaponBuffBonus;
        public double Coins;
        public double CoinsEarned;
        public double CoinsSpent;
        public double Kills;
        public double[] KillsByTier = new double[6];
        public int WeaponsBought;
        public int PerksBought;
        public bool HasPendingBreak;
        public double PendingBreakCoins;
        public double PendingBreakLoopCost;
        public double LoopCostToDate;
        public int LevelUpsTaken;
        public Dictionary<GlobalUpgradeData, int> GlobalPicks = new();
        public Dictionary<UpgradeData, int> SkillRanks = new();
        public List<string> PickLog = new();

        public SimPlayer Clone()
        {
            var c = (SimPlayer)MemberwiseClone();
            c.Stats = Stats.Clone();
            c.Weapon = Weapon.Clone();
            c.KillsByTier = (double[])KillsByTier.Clone();
            c.GlobalPicks = new Dictionary<GlobalUpgradeData, int>(GlobalPicks);
            c.SkillRanks = new Dictionary<UpgradeData, int>(SkillRanks);
            c.PickLog = new List<string>(PickLog);
            return c;
        }

        public double WeaponDps(BalanceSimScenario scenario)
            => Weapon.Dps(Stats, Skill.WeaponFireRateBonus, Skill.WeaponReloadBonus) * (1 + WeaponBuffBonus) * scenario.HitEfficiency;

        // Skill hits skip weapon crit but keep CharacterStats crit (DamageUtility:890-901).
        public double SkillDps(BalanceSimScenario scenario)
        {
            double perActivation = Skill.DamagePerActivation + SkillUpgradeDamage;
            if (perActivation <= 0)
                return 0;

            double cooldown = Skill.Cooldown / Math.Max(0.01, Stats.SkillCooldownMultiplier);
            double cycle = Math.Max(cooldown, Skill.ActiveDuration * Stats.SkillDurationMultiplier);
            double critFactor = 1 + Math.Clamp(Stats.CriticalChance, 0, 1) * (Stats.CriticalDamageMultiplier - 1);
            double damage = perActivation * Stats.DamageMultiplier * Stats.SkillDamageMultiplier * critFactor * (1 + SkillUpgradeBonus);

            return damage / Math.Max(0.1, cycle) * scenario.SkillUseEfficiency;
        }

        public double TotalDps(BalanceSimScenario scenario) => WeaponDps(scenario) + SkillDps(scenario);
    }

    public class SimEnemy
    {
        public EnemyDataAsset Data;
        public EnemyTier Tier;
        public double Hp;
        public double MaxHp;
        public double Cost;
        public double Exp;
        public double CoinValue;
        public double CoinChance;
        public AssetObject Source;
        public bool Leaker;
        public double RetireAt;
        public double EngageAt;
    }
}
