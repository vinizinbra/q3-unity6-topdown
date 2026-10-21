namespace Quantum
{
    using Photon.Deterministic;

    // Meta-progression Talents - see RuntimePlayer.Talents and docs/talents.md. Power/Exploration
    // levels are baked once into CharacterStats at spawn (ApplyPerPlayerTalents, called from
    // PlayerSpawnUtility.Spawn); SharedTalentRequirement gates resolve against the leader's
    // RuntimeConfig.WorldUnlocks (IsSatisfied, called from TalentGateSystem).
    public static unsafe class TalentUtility
    {
        // True if a SpawnEntityWithRequirement's Requirement is currently satisfied by the world
        // unlocks - does NOT roll Chance, see TalentGateSystem for that. Requirements with no menu
        // line yet (HeroChest, GlobalUpgradeChest, UnlockedRift, FindStones, Event) never pass.
        public static bool IsSatisfied(Frame f, SharedTalentRequirement requirement)
        {
            switch (requirement)
            {
                case SharedTalentRequirement.None: return true;
                case SharedTalentRequirement.WeaponChest:
                    return WorldUnlockUtility.IsUnlocked(f, WorldUnlock.RiftAnchor, MetaUnlockLevels.BaseWeaponChest);
                case SharedTalentRequirement.HeroChest:
                case SharedTalentRequirement.GlobalUpgradeChest:
                case SharedTalentRequirement.UnlockedRift:
                case SharedTalentRequirement.FindStones:
                case SharedTalentRequirement.Event:
                    return false;
                case SharedTalentRequirement.RepairStation:
                    return WorldUnlockUtility.IsUnlocked(f, WorldUnlock.RiftAnchor, MetaUnlockLevels.BaseRepairStation);
                default: return true;
            }
        }

        // TalentsConfig, or null (logged once by ApplyPerPlayerTalents) when RuntimeConfig has none.
        public static TalentsConfig GetConfig(Frame f)
        {
            return f.GameConfig.TalentsConfig.Id.IsValid ? f.FindAsset(f.GameConfig.TalentsConfig) : null;
        }

        // The meta-progression Talents of the player driving this entity (RuntimePlayer is input data,
        // identical on every client - safe to read mid-match). False for non-player entities.
        public static bool TryGetTalents(Frame f, EntityRef entity, out PlayerTalents talents)
        {
            talents = default;
            if (f.TryGet<PlayerLink>(entity, out PlayerLink link) == false)
                return false;

            RuntimePlayer runtimePlayer = f.GetPlayerData(link.Player);
            if (runtimePlayer == null)
                return false;

            talents = runtimePlayer.Talents;
            return true;
        }

        // Weapons line: perk rarity for a weapon rolled FOR this player (any source). Null = pool weights.
        public static WeaponTalentRarityTuning? ResolveWeaponPerkRarity(Frame f, EntityRef player)
        {
            TalentsConfig config = GetConfig(f);
            if (config == null || TryGetTalents(f, player, out PlayerTalents talents) == false)
                return null;
            return config.ResolveWeaponPerkRarity(talents.Exploration.WeaponsLevel);
        }

        // Joined slots only (f.MaxPlayerCount is the fixed slot count, GetPlayerData is null for an empty one).
        private static int CountSessionPlayers(Frame f)
        {
            int count = 0;
            for (int i = 0; i < f.MaxPlayerCount; i++)
            {
                if (f.GetPlayerData(i) != null)
                    count++;
            }

            return count;
        }

        public static void ApplyPerPlayerTalents(Frame f, EntityRef entity, RuntimePlayer runtimePlayer, CharacterStats* stats)
        {
            if (f.GameConfig.TalentsConfig.Id.IsValid == false)
            {
                Log.Error("[Talents] no TalentsConfig assigned on RuntimeConfig - Player* talents won't apply");
                return;
            }

            TalentsConfig config = f.FindAsset(f.GameConfig.TalentsConfig);
            FP step = config.PercentPerLevel / 100;

            PowerTalents power = runtimePlayer.Talents.Power;
            ExplorationTalents exploration = runtimePlayer.Talents.Exploration;

            ApplyBonus(&stats->DamageMultiplier, power.DamageLevel, step);
            ApplyReduction(&stats->DashCooldownMultiplier, power.SkillCooldownLevel, step);
            ApplyReduction(&stats->SkillCooldownMultiplier, power.SkillCooldownLevel, step);
            ApplyBonus(&stats->AttackSpeedMultiplier, power.FireRateLevel, step);
            ApplyBonus(&stats->ReloadSpeedMultiplier, power.ReloadSpeedLevel, step);
            ApplyFlat(&stats->CriticalChance, power.CriticalChanceLevel, step);
            ApplyBonus(&stats->CriticalDamageMultiplier, power.CriticalDamageLevel, step);
            ApplyFlat(&stats->DamageReduction, power.DamageReductionLevel, step);
            ApplyBonus(&stats->MoveSpeedMultiplier, power.MoveSpeedLevel, step);
            ApplyBonus(&stats->PickupRangeMultiplier, power.PickupRangeLevel, step);
            ApplyBonus(&stats->ExperienceGainMultiplier, power.XpGainLevel, step);
            ApplyBonus(&stats->CoinGainMultiplier, power.CoinGainLevel, config.CoinGainPercentPerLevel / 100);

            // Level-shaped Exploration lines - the only source of these stats at spawn.
            stats->Coins = TalentsConfig.Resolve(config.StartingGoldByLevel, exploration.StartingGoldLevel);
            stats->RerollQuantity = ClampByte(TalentsConfig.Resolve(config.RerollsByLevel, exploration.RerollsLevel));
            int reviveCharges = TalentsConfig.Resolve(config.SelfReviveChargesByLevel, exploration.ReviveLevel);
            if (CountSessionPlayers(f) == 1)
                reviveCharges += config.SoloSelfReviveBonus;
            stats->SelfReviveCharges = ClampByte(reviveCharges);

            // MaxHealthMultiplier/MaxShieldMultiplier scale CharacterData's baseline into
            // Health.MaxHealth/Shield.MaxShield once, not derived on read - the explicit Refresh
            // calls are required for the level bonus to actually show up (see CharacterStats.qtn's
            // own comment on these two fields).
            if (power.MaxHealthLevel > 0)
            {
                ApplyBonus(&stats->MaxHealthMultiplier, power.MaxHealthLevel, step);
                CharacterSystem.RefreshMaxHealth(f, entity);
            }

            if (power.MaxShieldLevel > 0)
            {
                ApplyBonus(&stats->MaxShieldMultiplier, power.MaxShieldLevel, step);
                CharacterSystem.RefreshMaxShield(f, entity);
            }
        }

        private static byte ClampByte(int value)
        {
            return (byte)(value < 0 ? 0 : value > byte.MaxValue ? byte.MaxValue : value);
        }

        private static void ApplyBonus(FP* stat, byte level, FP step)
        {
            if (level > 0)
            {
                *stat *= FP._1 + step * level;
            }
        }

        private static void ApplyReduction(FP* stat, byte level, FP step)
        {
            if (level > 0)
            {
                *stat *= FP._1 - step * level;
            }
        }

        private static void ApplyFlat(FP* stat, byte level, FP step)
        {
            if (level > 0)
            {
                *stat += step * level;
            }
        }
    }
}
