namespace Quantum
{
    using Photon.Deterministic;

    // Meta-progression Talents - see RuntimePlayer's own Player*/Has*/Can* fields and
    // docs/talents.md. Player* levels are baked once into CharacterStats at spawn
    // (ApplyPerPlayerTalents, called from PlayerSpawnUtility.Spawn); Has*/Can* flags are OR'd
    // across every connected player (ComputeSharedTalents, called from TalentGateSystem) to decide
    // what exists for the whole co-op group in the LobbyStart chunk.
    public static unsafe class TalentUtility
    {
        public static void ComputeSharedTalents(Frame f)
        {
            bool hasWeaponChest = false;
            bool hasHeroChest = false;
            bool hasGlobalUpgradeChest = false;
            bool hasUnlockedRift = false;
            bool canFindStones = false;
            bool hasEvent = false;

            for (int i = 0; i < f.MaxPlayerCount; i++)
            {
                // f.PlayerCount is the fixed max slot count for this session, not how many
                // players actually connected - GetPlayerData returns null for an unjoined slot
                // (same guard LevelGenerationSystem.SpawnPendingPlayers uses).
                RuntimePlayer runtimePlayer = f.GetPlayerData(i);

                if (runtimePlayer == null)
                    continue;

                hasWeaponChest |= runtimePlayer.Talents.HasWeaponChest;
                hasHeroChest |= runtimePlayer.Talents.HasHeroChest;
                hasGlobalUpgradeChest |= runtimePlayer.Talents.HasGlobalUpgradeChest;
                hasUnlockedRift |= runtimePlayer.Talents.HasUnlockedRift;
                canFindStones |= runtimePlayer.Talents.CanFindStones;
                hasEvent |= runtimePlayer.Talents.HasEvent;
            }

            f.Global->SharedHasWeaponChest = hasWeaponChest;
            f.Global->SharedHasHeroChest = hasHeroChest;
            f.Global->SharedHasGlobalUpgradeChest = hasGlobalUpgradeChest;
            f.Global->SharedHasUnlockedRift = hasUnlockedRift;
            f.Global->SharedCanFindStones = canFindStones;
            f.Global->SharedHasEvent = hasEvent;
        }

        // True if a SpawnEntityWithRequirement's Requirement is currently satisfied by the
        // resolved Global.Shared* aggregate - does NOT roll Chance, see TalentGateSystem for that.
        public static bool IsSatisfied(Frame f, SharedTalentRequirement requirement)
        {
            switch (requirement)
            {
                case SharedTalentRequirement.None: return true;
                // Rift Anchor L1 (menu Upgrades tab) or the legacy Inspector-set HasWeaponChest flag.
                case SharedTalentRequirement.WeaponChest:
                    return f.Global->SharedHasWeaponChest || WorldUnlockUtility.IsUnlocked(f, WorldUnlock.RiftAnchor, MetaUnlockLevels.BaseWeaponChest);
                case SharedTalentRequirement.HeroChest: return f.Global->SharedHasHeroChest;
                case SharedTalentRequirement.GlobalUpgradeChest: return f.Global->SharedHasGlobalUpgradeChest;
                case SharedTalentRequirement.UnlockedRift: return f.Global->SharedHasUnlockedRift;
                case SharedTalentRequirement.FindStones: return f.Global->SharedCanFindStones;
                case SharedTalentRequirement.Event: return f.Global->SharedHasEvent;
                case SharedTalentRequirement.RepairStation:
                    return WorldUnlockUtility.IsUnlocked(f, WorldUnlock.RiftAnchor, MetaUnlockLevels.BaseRepairStation);
                default: return true;
            }
        }

        // TalentsConfig, or null (logged once by ApplyPerPlayerTalents) when RuntimeConfig has none.
        public static TalentsConfig GetConfig(Frame f)
        {
            return f.RuntimeConfig.TalentsConfig.Id.IsValid ? f.FindAsset(f.RuntimeConfig.TalentsConfig) : null;
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
            return config.ResolveWeaponPerkRarity(talents.WeaponLevel);
        }

        public static void ApplyPerPlayerTalents(Frame f, EntityRef entity, RuntimePlayer runtimePlayer, CharacterStats* stats)
        {
            if (f.RuntimeConfig.TalentsConfig.Id.IsValid == false)
            {
                Log.Error("[Talents] no TalentsConfig assigned on RuntimeConfig - Player* talents won't apply");
                return;
            }

            TalentsConfig config = f.FindAsset(f.RuntimeConfig.TalentsConfig);
            FP step = config.PercentPerLevel / 100;

            ApplyBonus(&stats->DamageMultiplier, runtimePlayer.Talents.PlayerDamageLevel, step);
            ApplyReduction(&stats->DashCooldownMultiplier, runtimePlayer.Talents.PlayerCooldownLevel, step);
            ApplyReduction(&stats->SkillCooldownMultiplier, runtimePlayer.Talents.PlayerCooldownLevel, step);
            ApplyBonus(&stats->AttackSpeedMultiplier, runtimePlayer.Talents.PlayerFireRateLevel, step);
            ApplyBonus(&stats->ReloadSpeedMultiplier, runtimePlayer.Talents.PlayerReloadSpeedLevel, step);
            ApplyFlat(&stats->CriticalChance, runtimePlayer.Talents.PlayerCriticalChanceLevel, step);
            ApplyBonus(&stats->CriticalDamageMultiplier, runtimePlayer.Talents.PlayerCriticalDamageLevel, step);
            ApplyFlat(&stats->DamageReduction, runtimePlayer.Talents.PlayerDamageReductionLevel, step);
            ApplyBonus(&stats->MoveSpeedMultiplier, runtimePlayer.Talents.PlayerMoveSpeedLevel, step);
            ApplyBonus(&stats->PickupRangeMultiplier, runtimePlayer.Talents.PlayerPickupRangeLevel, step);
            ApplyBonus(&stats->ExperienceGainMultiplier, runtimePlayer.Talents.PlayerExperienceLevel, step);
            ApplyBonus(&stats->CoinGainMultiplier, runtimePlayer.Talents.PlayerCoinGainLevel, config.CoinGainPercentPerLevel / 100);

            // Level-shaped Exploration lines - added on top of the raw Inspector test counts
            // PlayerSpawnUtility.Spawn already copied in.
            stats->Coins += TalentsConfig.Resolve(config.StartingGoldByLevel, runtimePlayer.Talents.StartingGoldLevel);
            stats->RerollQuantity = ClampByte(stats->RerollQuantity + TalentsConfig.Resolve(config.RerollsByLevel, runtimePlayer.Talents.RerollsLevel));
            stats->SelfReviveCharges = ClampByte(stats->SelfReviveCharges + TalentsConfig.Resolve(config.SelfReviveChargesByLevel, runtimePlayer.Talents.ReviveLevel));

            // MaxHealthMultiplier/MaxShieldMultiplier scale CharacterData's baseline into
            // Health.MaxHealth/Shield.MaxShield once, not derived on read - the explicit Refresh
            // calls are required for the level bonus to actually show up (see CharacterStats.qtn's
            // own comment on these two fields).
            if (runtimePlayer.Talents.PlayerMaxHealthLevel > 0)
            {
                ApplyBonus(&stats->MaxHealthMultiplier, runtimePlayer.Talents.PlayerMaxHealthLevel, step);
                CharacterSystem.RefreshMaxHealth(f, entity);
            }

            if (runtimePlayer.Talents.PlayerMaxShieldLevel > 0)
            {
                ApplyBonus(&stats->MaxShieldMultiplier, runtimePlayer.Talents.PlayerMaxShieldLevel, step);
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
