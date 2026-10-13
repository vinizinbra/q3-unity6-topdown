using UnityEngine;

public enum PermanentUpgradeCategory
{
    Power,
    Exploration
}

/// <summary>Name + text of one level of a multi-step upgrade (Exploration lines: "Unlock Store", "Store Restock"...).</summary>
[System.Serializable]
public struct PermanentUpgradeLevelInfo
{
    public string title;
    [TextArea(2, 4)] public string description;
}

/// <summary>
/// One permanent (meta-progression) upgrade as the Upgrades tab presents it: name, icon, levels, cost,
/// and how its value reads. Listed on <see cref="GameDB"/>; the bought level lives in
/// <see cref="MetaProgressionSave"/> under <see cref="id"/>, and <see cref="TalentSaveMapper"/> carries it into
/// a match (see docs/talents.md, "Meta progression"). Not related to Quantum's in-run UpgradeData.
/// </summary>
[CreateAssetMenu(fileName = "PermanentUpgrade", menuName = "RiftRaiders/Permanent Upgrade")]
public class PermanentUpgradeData : ScriptableObject
{
    [Header("Identity")]
    public string displayName;

    [Tooltip("Small caption under the name in the detail panel (OFFENSE UPGRADE, DEFENSE UPGRADE...).")]
    public string kindLabel;

    [TextArea(2, 3)] public string description;
    public Sprite icon;

    [Tooltip("Tints the icon frame, the filled level pips and the kind caption.")]
    public Color accentColor = new Color(0.992f, 0.224f, 0.443f);

    [Header("Listing")]
    public PermanentUpgradeCategory category;

    [Tooltip("Rows with a different group get a divider line between them.")]
    public int group;

    [Header("Levels & cost")]
    [Min(1)] public int maxLevel = 5;
    public int baseCost = 100;

    [Tooltip("Extra cost added per level already owned (0 = flat price).")]
    public int costStep;

    [Header("Value shown (CURRENT / NEXT)")]
    [Tooltip("Value at level N = N x this.")]
    public float valuePerLevel = 5f;

    [Tooltip("{0} is the value, e.g. +{0}% or {0} chest.")]
    public string valueFormat = "+{0}%";

    [Tooltip("Optional format for any level other than 1 (e.g. {0} chests).")]
    public string valueFormatPlural;

    [Header("Per-level info (optional)")]
    [Tooltip("Index 0 = level 1. When filled, the detail panel shows the owned and the next level (title + description) "
             + "instead of numeric CURRENT/NEXT values. Keep its length equal to Max Level.")]
    public PermanentUpgradeLevelInfo[] levelInfos;

    [Tooltip("CURRENT card text while nothing is bought yet (level 0). Empty = \"Not unlocked yet.\"")]
    public string levelZeroText;

    [Header("Unlock requirement (both must hold; leave empty for none)")]
    [Tooltip("Another line that must be owned first, e.g. Store for Weapons/Gunsmith/Co-op.")]
    public PermanentUpgradeId requiredUpgrade;

    [Tooltip("Level of Required Upgrade needed (ignored when Required Upgrade is None).")]
    [Min(0)] public int requiredUpgradeLevel = 1;

    [Tooltip("Total levels bought across every line of this line's category (Exploration) needed first. "
             + "Every bought level counts as 1, whatever the line, including the ones used for other requirements.")]
    [Min(0)] public int requiredCategoryLevels;

    [Header("Identity in the save / simulation")]
    [Tooltip("Save key + what TalentSaveMapper maps this line to (a RuntimePlayer.Talents level or a "
             + "RuntimeConfig.WorldUnlocks level). Never change it on a shipped line - saved levels are keyed by it.")]
    public PermanentUpgradeId id;

    public string Name => string.IsNullOrEmpty(displayName) ? name : displayName;

    public int CostFor(int level) => baseCost + costStep * Mathf.Max(0, level);

    public bool HasLevelInfo => levelInfos != null && levelInfos.Length > 0;

    /// <summary>The info of level <paramref name="level"/> (1-based), clamped to the authored levels.</summary>
    public PermanentUpgradeLevelInfo LevelInfo(int level) => levelInfos[Mathf.Clamp(level, 1, levelInfos.Length) - 1];

    public string ValueLabel(int level)
    {
        if (HasLevelInfo)
            return level <= 0 ? "Locked" : levelInfos[Mathf.Min(level, levelInfos.Length) - 1].title;

        string format = level != 1 && !string.IsNullOrEmpty(valueFormatPlural) ? valueFormatPlural : valueFormat;
        return string.Format(format, (valuePerLevel * level).ToString("0.##"));
    }
}
