using System.Collections.Generic;
using NaughtyAttributes;
using Photon.Deterministic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "How is my build doing" readout for HeroInfoPopupWidget - two columns (offense / defense & utility),
// one row per stat: icon, label, then value plus a colored "+15%" delta on the right (green = better,
// red = worse; cooldowns invert so lower reads green).
//
// Rows are built in code under `content` from the fontSource's font, so the widget needs no per-row
// prefab. Every row is always shown, so the grid never reflows while the popup is open. Deltas are
// relative to a neutral 1.0 multiplier, i.e. they already include the hero's own authored baseline.
//
// Bound externally via Initialize (HeroInfoPopupWidget owns the binding) - no self-binding default.
public class HeroBuildStatsWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Parent of the two generated columns - a HorizontalLayoutGroup is added if it has no layout group.")]
    private RectTransform content;

    [SerializeField, Tooltip("Any existing TMP_Text whose font/material the generated rows copy.")]
    private TMP_Text fontSource;

    [SerializeField] private float fontSize = 20f;
    [SerializeField] private float headerFontSize = 22f;
    [SerializeField] private float rowHeight = 26f;
    [SerializeField] private float valueWidth = 96f;
    [SerializeField] private float iconSize = 26f;
    [SerializeField, Tooltip("The +15% delta's size relative to the value, so the number stays the thing you read first.")]
    private int deltaSizePercent = 85;
    [SerializeField, Tooltip("Off when the panel already carries its own section header (e.g. a STATS banner) above the columns.")]
    private bool showColumnHeaders = true;
    [SerializeField, Tooltip("Thin vertical line between the two columns - alpha 0 for none.")]
    private Color columnDividerColor = new Color(1f, 1f, 1f, 0f);
    [SerializeField] private Color headerColor = new Color(0.99f, 0.22f, 0.44f); // #FD3971
    [SerializeField] private Color labelColor = new Color(0.85f, 0.85f, 0.85f);
    [SerializeField] private Color iconColor = Color.white;
    [SerializeField] private Color valueColor = Color.white;
    [SerializeField] private Color neutralColor = new Color(0.6f, 0.6f, 0.6f);
    [SerializeField] private Color positiveColor = new Color(0.35f, 0.9f, 0.4f);
    [SerializeField] private Color negativeColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField, Tooltip("Delta color while a timed bonus is running (Overdrive, Juggernaut, Flow, Energy Drink, Danger Pay...) so a temporary spike reads differently from the permanent build.")]
    private Color buffedColor = new Color(1f, 0.78f, 0.29f);

    [Header("Icons (optional - a stat without one gets no icon slot)")]
    [SerializeField] private Sprite damageIcon;
    [SerializeField] private Sprite critChanceIcon;
    [SerializeField] private Sprite critDamageIcon;
    [SerializeField] private Sprite areaIcon;
    [SerializeField] private Sprite maxHealthIcon;
    [SerializeField] private Sprite moveSpeedIcon;
    [SerializeField] private Sprite skillCooldownIcon;
    [SerializeField] private Sprite dashCooldownIcon;

    [SerializeField] private EntityRef _entityRef;

    private struct StatRow
    {
        public GameObject Root;
        public TMP_Text Value;
    }

    private enum Stat
    {
        Damage, CritChance, CritDamage, Area,
        MaxHealth, MoveSpeed, SkillCooldown, DashCooldown
    }

    private readonly Dictionary<Stat, StatRow> _rows = new Dictionary<Stat, StatRow>();
    private bool _built;

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    public override void QUpdate(QuantumGame game)
    {
        if (content == null)
            return;

        EnsureBuilt();

        Frame frame = game.Frames.Predicted;

        if (frame.TryGet<CharacterStats>(_entityRef, out var stats) == false)
            return;

        RefreshDamage(frame);

        DamageUtility.ResolveBaselineCritical(frame, _entityRef, DamageSource.Weapon, out FP critChance, out FP critMultiplier);
        bool critUp = critChance > FP._0;
        // Percentage points over the base don't fit next to the label, so a raised chance is just tinted.
        string critText = $"{Pct(critChance)}%";
        SetRow(Stat.CritChance, critUp ? $"<color=#{ColorUtility.ToHtmlStringRGB(positiveColor)}>{critText}</color>" : critText, string.Empty, critUp ? 1 : 0, true);
        SetRow(Stat.CritDamage, $"x{critMultiplier.AsFloat:0.0#}", string.Empty, critMultiplier > FP._1 ? 1 : 0, true);

        Multiplier(Stat.Area, stats.AreaRadiusMultiplier, true, true);

        if (frame.TryGet<Health>(_entityRef, out var health))
        {
            string delta = DeltaPercent(stats.MaxHealthMultiplier, true, out int hpSign);
            SetRow(Stat.MaxHealth, Mathf.CeilToInt(health.MaxHealth.AsFloat).ToString(), delta, hpSign, true);
        }

        RefreshMoveSpeed(frame);

        frame.TryGet<CharacterSkills>(_entityRef, out var skills);
        Cooldown(frame, Stat.SkillCooldown, SkillSlotId.HeroSkill, skills.HeroSkill.Skill, stats.SkillCooldownMultiplier);
        Cooldown(frame, Stat.DashCooldown, SkillSlotId.DashSkill, skills.DashSkill.Skill, stats.DashCooldownMultiplier);
    }

    // The live total (upgrades + weapon weight + hero skill states + timed buffs) - skills like Overdrive
    // write straight into CharacterStats.MoveSpeedMultiplier, so reading it per frame already follows
    // them. The delta turns gold while any timed bonus is up, so a temporary spike can't be mistaken for
    // the permanent build.
    private void RefreshMoveSpeed(Frame frame)
    {
        FP total = StatUtility.ResolveBaselineMoveSpeedMultiplier(frame, _entityRef);
        string delta = DeltaPercent(total, true, out int sign);

        FP timed = StatusEffectUtility.GetTempMoveSpeedMultiplier(frame, _entityRef)
            * MutationModifierUtility.ResolveLiveMoveSpeedMultiplier(frame, _entityRef);
        bool skillBuff = HeroSkillStateUtility.TryGet(frame, _entityRef, out HeroSkillState skill) && skill.Active && skill.Full;

        if (sign > 0 && (Pct(timed - FP._1) != 0 || skillBuff))
            sign = 2;

        SetRow(Stat.MoveSpeed, delta.Length == 0 ? $"x{total.AsFloat:0.0#}" : string.Empty, delta, sign, true);
    }

    private void RefreshDamage(Frame frame)
    {
        if (frame.TryGet<Weapon>(_entityRef, out var weapon) == false || weapon.WeaponData.IsValid == false)
        {
            SetRow(Stat.Damage, "-", string.Empty, 0, true);
            return;
        }

        WeaponDataAsset weaponData = frame.FindAsset(weapon.WeaponData);
        FP baseline = weaponData.Damage;
        FP final = baseline * weapon.DamageMultiplier * DamageUtility.ResolveBaselineDamageMultiplier(frame, _entityRef, DamageSource.Weapon);

        // The number shown is the TOTAL per trigger pull - for a multi-pellet weapon every pellet summed, so a
        // shotgun reads as what one shot is worth rather than one pellet. Each pellet is rounded UP first,
        // the same way DamageNumberUiWidget prints each pellet's own number (and HealthUiWidget rounds
        // health), so the total is exactly the sum of the numbers the player sees pop. A multi-pellet
        // weapon shows its "(6x9)" split in gray; the tint alone says the build moved it - never a "+N"
        // delta, which would read as additive on top of the total.
        int pellets = Mathf.Max(1, weaponData.PelletCount);
        int perPellet = Mathf.CeilToInt(final.AsFloat);
        int basePerPellet = Mathf.CeilToInt(baseline.AsFloat);
        int total = perPellet * pellets;
        int sign = perPellet == basePerPellet ? 0 : perPellet > basePerPellet ? 1 : -1;
        Color totalColor = sign > 0 ? positiveColor : sign < 0 ? negativeColor : valueColor;
        string value = $"<color=#{ColorUtility.ToHtmlStringRGB(totalColor)}>{total}</color>";
        string reference = pellets > 1 ? $"({pellets}x{perPellet})" : string.Empty;

        // sign 0 on the delta channel renders it in the neutral gray; the total carries the tint itself.
        SetRow(Stat.Damage, value, reference, 0, true);
    }

    // Pure multipliers have no number worth showing next to their delta ("x1.25 +25%" says it twice),
    // so the value only appears as "x1.0" when there's no delta at all.
    private void Multiplier(Stat stat, FP value, bool higherIsBetter, bool alwaysShow)
    {
        string delta = DeltaPercent(value, higherIsBetter, out int sign);
        SetRow(stat, delta.Length == 0 ? $"x{value.AsFloat:0.0#}" : string.Empty, delta, sign, alwaysShow);
    }

    // The cooldown stats are RATE multipliers - StatUtility.GetSkillCooldown divides the base cooldown by
    // them, so a 1.15 stat is a 13% SHORTER cooldown, not 15% longer. Shown as the real seconds (the
    // same GetSkillCooldown the sim arms the slot with), tinted green when shorter than authored - the
    // seconds already say how much, so no separate % (which also wouldn't fit the column).
    private void Cooldown(Frame frame, Stat stat, SkillSlotId slot, AssetRef<SkillData> skillRef, FP rateMultiplier)
    {
        if (rateMultiplier <= FP._0 || skillRef.IsValid == false)
        {
            SetRow(stat, "-", string.Empty, 0, true);
            return;
        }

        FP seconds = StatUtility.GetSkillCooldown(frame, _entityRef, slot, frame.FindAsset(skillRef).Cooldown);
        DeltaPercent(FP._1 / rateMultiplier, false, out int sign);
        Color tint = sign > 0 ? positiveColor : sign < 0 ? negativeColor : valueColor;
        SetRow(stat, $"<color=#{ColorUtility.ToHtmlStringRGB(tint)}>{seconds.AsFloat:0.#}s</color>", string.Empty, 0, true);
    }

    private static int Pct(FP value) => Mathf.RoundToInt(value.AsFloat * 100f);

    // "+15%" off a 1.0-based multiplier. `sign` is +1 when the change is an improvement, -1 when worse,
    // 0 when neutral (flips for stats where lower is better).
    private static string DeltaPercent(FP multiplier, bool higherIsBetter, out int sign)
    {
        int pct = Pct(multiplier - FP._1);
        sign = pct == 0 ? 0 : (pct > 0) == higherIsBetter ? 1 : -1;
        return pct == 0 ? string.Empty : $"{(pct > 0 ? "+" : string.Empty)}{pct}%";
    }

    private void SetRow(Stat stat, string value, string delta, int sign, bool alwaysShow)
    {
        if (_rows.TryGetValue(stat, out var row) == false)
            return;

        bool visible = alwaysShow || sign != 0;

        if (row.Root.activeSelf != visible)
            row.Root.SetActive(visible);

        if (visible == false)
            return;

        string deltaHex = ColorUtility.ToHtmlStringRGB(sign > 1 ? buffedColor : sign > 0 ? positiveColor : sign < 0 ? negativeColor : neutralColor);
        string valueHex = ColorUtility.ToHtmlStringRGB(sign == 0 && delta.Length == 0 && (value.StartsWith("x") || value == "-") ? neutralColor : valueColor);

        string text = value.Length > 0 ? $"<color=#{valueHex}>{value}</color>" : string.Empty;

        if (delta.Length > 0)
            text += (text.Length > 0 ? " " : string.Empty) + $"<size={deltaSizePercent}%><color=#{deltaHex}>{delta}</color></size>";

        row.Value.text = text;
    }

    private void EnsureBuilt()
    {
        if (_built)
            return;

        _built = true;
        Build(false);
    }

    // Inspector mockup: builds the same rows with sample values so the layout can be judged (and tuned)
    // in the prefab without entering Play. EnsureBuilt clears these on its own first run, so the saved
    // preview never leaks into a real match.
    [Button("Build Preview")]
    private void BuildPreview()
    {
        if (content == null)
            return;

        _rows.Clear();
        Build(true);

        // DontSave: the mockup must never be written into the prefab/scene - a prefab instance can't
        // delete children it inherits, so a saved mockup would stack under every later rebuild.
        foreach (Transform child in content.GetComponentsInChildren<Transform>(true))
        {
            if (child != content)
                child.gameObject.hideFlags = HideFlags.DontSave;
        }

        const string up = "59E566";
        const string down = "FF5959";
        Preview(Stat.Damage, $"<color=#{up}>31</color>");
        Preview(Stat.CritChance, $"<color=#{up}>25%</color>");
        Preview(Stat.CritDamage, "x1.5");
        Preview(Stat.Area, $"<color=#{up}>+15%</color>");
        Preview(Stat.MaxHealth, $"120 <size=85%><color=#{up}>+20%</color></size>");
        Preview(Stat.MoveSpeed, $"<color=#{down}>-10%</color>");
        Preview(Stat.SkillCooldown, $"<color=#{up}>10.2s</color>");
        Preview(Stat.DashCooldown, "3s");
    }

    [Button("Clear Preview")]
    private void ClearPreview()
    {
        if (content == null)
            return;

        _rows.Clear();
        ClearChildren(true);
    }

    private void Preview(Stat stat, string text)
    {
        if (_rows.TryGetValue(stat, out var row))
            row.Value.text = text;
    }

    private void ClearChildren(bool immediate)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            GameObject stale = content.GetChild(i).gameObject;

            if (immediate)
            {
                DestroyImmediate(stale);
                continue;
            }

            stale.SetActive(false);
            Destroy(stale);
        }
    }

    private void Build(bool immediate)
    {
        // A Canvas cloned after it already built (or an older serialized copy, or the saved preview)
        // carries rows of its own; building on top of them would stack a second set behind the new one.
        ClearChildren(immediate);

        if (content.GetComponent<LayoutGroup>() == null)
        {
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 16f;
        }

        Transform offense = AddColumn("Offense", "OFFENSE");
        Add(offense, Stat.Damage, "Damage", damageIcon);
        Add(offense, Stat.CritChance, "Crit Chance", critChanceIcon);
        Add(offense, Stat.CritDamage, "Crit Damage", critDamageIcon);
        Add(offense, Stat.Area, "Area", areaIcon);

        if (columnDividerColor.a > 0f)
            AddColumnDivider();

        Transform defense = AddColumn("Defense", "DEFENSE & UTILITY");
        Add(defense, Stat.MaxHealth, "Max Health", maxHealthIcon);
        Add(defense, Stat.MoveSpeed, "Move Speed", moveSpeedIcon);
        Add(defense, Stat.SkillCooldown, "Skill Cooldown", skillCooldownIcon);
        Add(defense, Stat.DashCooldown, "Dash Cooldown", dashCooldownIcon);
    }

    private Transform AddColumn(string name, string title)
    {
        var column = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        column.layer = content.gameObject.layer;
        column.transform.SetParent(content, false);

        var group = column.GetComponent<VerticalLayoutGroup>();
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        group.spacing = 0f;

        column.GetComponent<LayoutElement>().flexibleWidth = 1f;

        if (showColumnHeaders)
        {
            TMP_Text header = CreateText(column.transform, "Header", title, TextAlignmentOptions.MidlineLeft, headerColor, 0f, headerFontSize);
            header.fontStyle = FontStyles.Bold;
            SetHeight(header.GetComponent<LayoutElement>(), rowHeight + 4f);
        }

        return column.transform;
    }

    private void AddColumnDivider()
    {
        var divider = new GameObject("ColumnDivider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        divider.layer = content.gameObject.layer;
        divider.transform.SetParent(content, false);

        var image = divider.GetComponent<Image>();
        image.color = columnDividerColor;
        image.raycastTarget = false;

        var element = divider.GetComponent<LayoutElement>();
        element.minWidth = 2f;
        element.preferredWidth = 2f;
        element.flexibleWidth = 0f;
    }

    private void Add(Transform column, Stat stat, string label, Sprite icon)
    {
        var rowObject = new GameObject(stat.ToString(), typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowObject.layer = content.gameObject.layer;
        rowObject.transform.SetParent(column, false);

        var group = rowObject.GetComponent<HorizontalLayoutGroup>();
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        // Must stay false: a force-expanding row reports flexible height to the column and gets
        // stretched to fill it, which is what spread the rows far apart.
        group.childForceExpandHeight = false;
        group.spacing = 12f;

        SetHeight(rowObject.GetComponent<LayoutElement>(), rowHeight);
        group.childAlignment = TextAnchor.MiddleLeft;

        if (icon != null)
            CreateIcon(rowObject.transform, icon);

        TMP_Text labelText = CreateText(rowObject.transform, "Label", label, TextAlignmentOptions.MidlineLeft, labelColor, 1f, fontSize);
        SetHeight(labelText.GetComponent<LayoutElement>(), rowHeight);
        TMP_Text valueText = CreateText(rowObject.transform, "Value", string.Empty, TextAlignmentOptions.MidlineRight, valueColor, 0f, fontSize);
        valueText.fontStyle = FontStyles.Bold;
        valueText.GetComponent<LayoutElement>().minWidth = valueWidth;
        SetHeight(valueText.GetComponent<LayoutElement>(), rowHeight);

        _rows[stat] = new StatRow { Root = rowObject, Value = valueText };
    }

    // TMP's own preferred height is ~1.3x the font size, which is what made rows look double spaced -
    // pinning min and preferred makes rowHeight the actual pitch.
    private static void SetHeight(LayoutElement element, float height)
    {
        element.minHeight = height;
        element.preferredHeight = height;
    }

    private void CreateIcon(Transform parent, Sprite sprite)
    {
        var go = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = iconColor;
        image.preserveAspect = true;
        image.raycastTarget = false;

        var element = go.GetComponent<LayoutElement>();
        element.minWidth = iconSize;
        element.preferredWidth = iconSize;
        element.flexibleWidth = 0f;
        SetHeight(element, iconSize);
    }

    private TMP_Text CreateText(Transform parent, string name, string text, TextAlignmentOptions alignment, Color color, float flexibleWidth, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (fontSource != null)
        {
            tmp.font = fontSource.font;
            tmp.fontSharedMaterial = fontSource.fontSharedMaterial;
        }

        tmp.fontSize = size;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.text = text;
        tmp.richText = true;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        go.GetComponent<LayoutElement>().flexibleWidth = flexibleWidth;
        return tmp;
    }
}
