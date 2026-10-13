using System;
using System.Collections.Generic;
using Playtime.Core;
using QuantumUser.View.Util;
using UnityEngine;

/// <summary>
/// The local player's permanent progression: the Rift Shard wallet and the bought level of every menu
/// Upgrades tab line (keyed by <see cref="PermanentUpgradeId"/> name, so renaming an asset never loses a
/// level). One JSON pref. Read into a match by <see cref="TalentSaveMapper"/>. See docs/talents.md.
/// </summary>
public static class MetaProgressionSave
{
    private const string LogTag = "MetaProgression";

    [Serializable]
    private class SaveData
    {
        public int riftShards;
        public List<LevelEntry> levels = new List<LevelEntry>();
    }

    [Serializable]
    private class LevelEntry
    {
        public string id;
        public int level;
    }

    private static readonly PlayerPrefObject<SaveData> Pref = new PlayerPrefObject<SaveData>("meta_progression");

    /// <summary>Raised after any wallet or level change.</summary>
    public static event Action Changed;

    private static SaveData data;

    private static SaveData Data
    {
        get
        {
            if (data == null)
            {
                data = Pref.Value ?? new SaveData();
                data.levels ??= new List<LevelEntry>();
            }

            return data;
        }
    }

    public static int RiftShards => Data.riftShards;

    /// <summary>
    /// The level a line counts as. With the META_PLAYTEST define on, GameDB.PlaytestConfig's level is a floor
    /// (max of saved and playtest) - see MetaProgressionPlaytestConfig.
    /// </summary>
    public static int GetLevel(PermanentUpgradeId id)
    {
        int saved = GetSavedLevel(id);
#if META_PLAYTEST
        MetaProgressionPlaytestConfig playtest = GameDB.Instance.PlaytestConfig;
        if (playtest != null)
            return Mathf.Max(saved, playtest.GetLevel(id));
#endif
        return saved;
    }

    private static int GetSavedLevel(PermanentUpgradeId id)
    {
        string key = id.ToString();
        foreach (LevelEntry entry in Data.levels)
        {
            if (entry.id == key)
                return entry.level;
        }

        return 0;
    }

    public static bool CanAfford(PermanentUpgradeData upgrade)
    {
        int level = GetLevel(upgrade.id);
        return level < upgrade.maxLevel && Data.riftShards >= upgrade.CostFor(level);
    }

    /// <summary>Total levels bought across every GameDB line of one category (each bought level counts 1).</summary>
    public static int CountLevels(PermanentUpgradeCategory category)
    {
        int total = 0;
        foreach (PermanentUpgradeData upgrade in GameDB.Instance.PermanentUpgrades)
        {
            if (upgrade != null && upgrade.category == category && upgrade.id != PermanentUpgradeId.None)
                total += Mathf.Clamp(GetLevel(upgrade.id), 0, upgrade.maxLevel);
        }

        return total;
    }

    /// <summary>
    /// Why this line can't be upgraded yet (its <see cref="PermanentUpgradeData.requiredUpgrade"/> /
    /// <see cref="PermanentUpgradeData.requiredCategoryLevels"/>), or null once unlocked. Levels already owned
    /// stay owned either way - a lock only stops buying the next one.
    /// </summary>
    public static string GetLockReason(PermanentUpgradeData upgrade)
    {
        if (upgrade.requiredUpgrade != PermanentUpgradeId.None)
        {
            int needed = Mathf.Max(1, upgrade.requiredUpgradeLevel);
            if (GetLevel(upgrade.requiredUpgrade) < needed)
            {
                PermanentUpgradeData required = GameDB.Instance.FindUpgrade(upgrade.requiredUpgrade);
                string name = required != null ? required.Name : upgrade.requiredUpgrade.ToString();
                return $"Requires {name} Lv. {needed}";
            }
        }

        if (upgrade.requiredCategoryLevels > 0)
        {
            int owned = CountLevels(upgrade.category);
            if (owned < upgrade.requiredCategoryLevels)
                return $"Requires {upgrade.requiredCategoryLevels} {upgrade.category} upgrades ({owned}/{upgrade.requiredCategoryLevels})";
        }

        return null;
    }

    public static bool IsUnlocked(PermanentUpgradeData upgrade) => GetLockReason(upgrade) == null;

    /// <summary>Spends the next level's cost and raises the level. False when locked, maxed or short on shards.</summary>
    public static bool TryPurchase(PermanentUpgradeData upgrade)
    {
        if (upgrade == null || upgrade.id == PermanentUpgradeId.None)
        {
            LogHelper.Warn(LogTag, $"{(upgrade != null ? upgrade.name : "null")} has no id - can't be bought.");
            return false;
        }

        if (!IsUnlocked(upgrade) || !CanAfford(upgrade))
            return false;

        int level = GetLevel(upgrade.id); // buying on top of a playtest floor saves floor + 1
        Data.riftShards -= upgrade.CostFor(level);
        SetLevelInternal(upgrade.id, level + 1);
        Save();
        LogHelper.Log(LogTag, $"Bought {upgrade.id} L{level + 1} - {Data.riftShards} Rift Shards left.");
        return true;
    }

    public static void AddRiftShards(int amount)
    {
        if (amount == 0)
            return;

        Data.riftShards = Mathf.Max(0, Data.riftShards + amount);
        Save();
    }

    public static void SetLevel(PermanentUpgradeId id, int level)
    {
        SetLevelInternal(id, level);
        Save();
    }

    /// <summary>Test helper: every GameDB line at its max level.</summary>
    public static void MaxAll()
    {
        foreach (PermanentUpgradeData upgrade in GameDB.Instance.PermanentUpgrades)
        {
            if (upgrade != null && upgrade.id != PermanentUpgradeId.None)
                SetLevelInternal(upgrade.id, upgrade.maxLevel);
        }

        Save();
    }

    /// <summary>
    /// Test helper: writes GameDB.PlaytestConfig's levels INTO the save (raising only, never lowering) - the
    /// no-define alternative to META_PLAYTEST, e.g. from the cheat menu on a tester's device.
    /// </summary>
    public static void ApplyPlaytestProfile()
    {
        MetaProgressionPlaytestConfig playtest = GameDB.Instance.PlaytestConfig;
        if (playtest == null)
            return;

        foreach (MetaProgressionPlaytestConfig.Entry entry in playtest.levels)
        {
            if (entry.id != PermanentUpgradeId.None && entry.level > GetSavedLevel(entry.id))
                SetLevelInternal(entry.id, entry.level);
        }

        Save();
    }

    /// <summary>Test helper: wallet and every level back to zero.</summary>
    public static void ResetAll()
    {
        data = new SaveData();
        Save();
    }

    private static void SetLevelInternal(PermanentUpgradeId id, int level)
    {
        string key = id.ToString();
        foreach (LevelEntry entry in Data.levels)
        {
            if (entry.id == key)
            {
                entry.level = Mathf.Max(0, level);
                return;
            }
        }

        Data.levels.Add(new LevelEntry { id = key, level = Mathf.Max(0, level) });
    }

    private static void Save()
    {
        Pref.Value = Data;
        Changed?.Invoke();
    }
}
