using UnityEngine;

public enum PermanentUpgradeCategory
{
    Power,
    Exploration
}

/// <summary>
/// One permanent (meta-progression) upgrade as the Upgrades tab presents it: name, icon, levels, cost,
/// and how its value reads. UI-facing only for now - it carries no gameplay. It is meant to be translated
/// into the Talents carried on RuntimePlayer (see docs/talents.md); <see cref="talentKey"/> names the
/// talent field it will drive. Not related to Quantum's in-run UpgradeData.
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

    [Header("Talent mapping (future)")]
    [Tooltip("Name of the talent field this drives, e.g. PlayerDamageLevel or HasWeaponChest. Informational until save data exists.")]
    public string talentKey;

    [Header("Mock (until there is save data)")]
    [Min(0)] public int mockLevel;

    public string Name => string.IsNullOrEmpty(displayName) ? name : displayName;

    public int CostFor(int level) => baseCost + costStep * Mathf.Max(0, level);

    public string ValueLabel(int level)
    {
        string format = level != 1 && !string.IsNullOrEmpty(valueFormatPlural) ? valueFormatPlural : valueFormat;
        return string.Format(format, (valuePerLevel * level).ToString("0.##"));
    }
}
