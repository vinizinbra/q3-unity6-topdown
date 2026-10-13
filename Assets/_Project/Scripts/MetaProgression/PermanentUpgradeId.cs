/// <summary>
/// Stable identity of a permanent (menu Upgrades tab) line: the save key and what TalentSaveMapper switches
/// on. Serialized by value on PermanentUpgradeData - append only, never reorder.
/// </summary>
public enum PermanentUpgradeId
{
    None = 0,

    // Power
    Damage = 1,
    SkillCooldown = 2,
    FireRate = 3,
    ReloadSpeed = 4,
    CriticalChance = 5,
    CriticalDamage = 6,
    MaxHealth = 7,
    MaxShield = 8,
    DamageReduction = 9,
    MoveSpeed = 10,
    PickupRange = 11,
    XpGain = 12,
    CoinGain = 13,

    // Exploration - world lines (RuntimeConfig.WorldUnlocks, the leader's levels)
    Store = 100,
    Gunsmith = 101,
    Challenges = 102,
    HealingShrine = 103,
    SacrificeRift = 104,
    RiftAnchor = 105,

    // Exploration - per-player lines (RuntimePlayer.Talents)
    Weapons = 200,
    StartingGold = 201,
    Rerolls = 202,
    Revive = 203,
    Coop = 204,
}
