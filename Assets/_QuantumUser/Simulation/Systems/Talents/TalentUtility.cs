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

            for (int i = 0; i < f.PlayerCount; i++)
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
                case SharedTalentRequirement.WeaponChest: return f.Global->SharedHasWeaponChest;
                case SharedTalentRequirement.HeroChest: return f.Global->SharedHasHeroChest;
                case SharedTalentRequirement.GlobalUpgradeChest: return f.Global->SharedHasGlobalUpgradeChest;
                case SharedTalentRequirement.UnlockedRift: return f.Global->SharedHasUnlockedRift;
                case SharedTalentRequirement.FindStones: return f.Global->SharedCanFindStones;
                case SharedTalentRequirement.Event: return f.Global->SharedHasEvent;
                default: return true;
            }
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
