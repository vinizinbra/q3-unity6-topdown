using Quantum;
using UnityEngine;

/// <summary>
/// The one place that turns <see cref="MetaProgressionSave"/> levels into what a match carries: per-player
/// lines onto <see cref="PlayerTalents"/>, world lines onto <see cref="WorldUnlocks"/>. Levels only - what a
/// level is worth is TalentsConfig's job inside the simulation. See docs/talents.md, "Meta progression".
/// </summary>
public static class TalentSaveMapper
{
    public static void ApplyPlayer(ref PlayerTalents talents)
    {
        talents.Power = new PowerTalents
        {
            DamageLevel = Level(PermanentUpgradeId.Damage),
            SkillCooldownLevel = Level(PermanentUpgradeId.SkillCooldown),
            FireRateLevel = Level(PermanentUpgradeId.FireRate),
            ReloadSpeedLevel = Level(PermanentUpgradeId.ReloadSpeed),
            CriticalChanceLevel = Level(PermanentUpgradeId.CriticalChance),
            CriticalDamageLevel = Level(PermanentUpgradeId.CriticalDamage),
            MaxHealthLevel = Level(PermanentUpgradeId.MaxHealth),
            MaxShieldLevel = Level(PermanentUpgradeId.MaxShield),
            DamageReductionLevel = Level(PermanentUpgradeId.DamageReduction),
            MoveSpeedLevel = Level(PermanentUpgradeId.MoveSpeed),
            PickupRangeLevel = Level(PermanentUpgradeId.PickupRange),
            XpGainLevel = Level(PermanentUpgradeId.XpGain),
            CoinGainLevel = Level(PermanentUpgradeId.CoinGain),
        };

        talents.Exploration = new ExplorationTalents
        {
            WeaponsLevel = Level(PermanentUpgradeId.Weapons),
            StartingGoldLevel = Level(PermanentUpgradeId.StartingGold),
            RerollsLevel = Level(PermanentUpgradeId.Rerolls),
            ReviveLevel = Level(PermanentUpgradeId.Revive),
            CoopLevel = Level(PermanentUpgradeId.Coop),
        };
    }

    public static WorldUnlocks BuildWorld()
    {
        return new WorldUnlocks
        {
            Enforce = true,
            StoreLevel = Level(PermanentUpgradeId.Store),
            GunsmithLevel = Level(PermanentUpgradeId.Gunsmith),
            ChallengesLevel = Level(PermanentUpgradeId.Challenges),
            HealingShrineLevel = Level(PermanentUpgradeId.HealingShrine),
            SacrificeRiftLevel = Level(PermanentUpgradeId.SacrificeRift),
            RiftAnchorLevel = Level(PermanentUpgradeId.RiftAnchor),
        };
    }

    // Saved level clamped to the line's authored max (a line shortened later never ships a stale level).
    private static byte Level(PermanentUpgradeId id)
    {
        int level = MetaProgressionSave.GetLevel(id);
        PermanentUpgradeData upgrade = GameDB.Instance.FindUpgrade(id);
        if (upgrade != null)
            level = Mathf.Min(level, upgrade.maxLevel);
        return (byte)Mathf.Clamp(level, 0, byte.MaxValue);
    }

    // Wire format for SyncMatchRoom (leader -> party): Photon hashtables carry a byte[] natively.
    public static byte[] Pack(WorldUnlocks world)
    {
        return new[]
        {
            world.StoreLevel, world.GunsmithLevel, world.ChallengesLevel,
            world.HealingShrineLevel, world.SacrificeRiftLevel, world.RiftAnchorLevel
        };
    }

    public static WorldUnlocks Unpack(byte[] packed)
    {
        var world = new WorldUnlocks { Enforce = true };
        if (packed == null || packed.Length < 6)
            return world;

        world.StoreLevel = packed[0];
        world.GunsmithLevel = packed[1];
        world.ChallengesLevel = packed[2];
        world.HealingShrineLevel = packed[3];
        world.SacrificeRiftLevel = packed[4];
        world.RiftAnchorLevel = packed[5];
        return world;
    }
}
