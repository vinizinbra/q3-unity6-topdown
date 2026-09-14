using Photon.Deterministic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row in RunResultPopup's team-damage breakdown (co-op only) - hero name, compact-formatted
// damage dealt, percentage of the whole team's damage, and a fill slider tinted with that hero's
// own CharacterData.RingColor. Purely presentational: RunResultManager resolves each connected
// player's hero/damage and RunResultPopup instantiates + Setup()s one of these per player, sorted
// by damage descending.
public class TeamDamageWidget : MonoBehaviour
{
    [SerializeField] private TMP_Text heroNameText;
    [SerializeField] private TMP_Text damageText;
    [SerializeField] private TMP_Text percentageText;
    [SerializeField] private Slider damageSlider;
    [SerializeField] private Image sliderFillImage;

    public void Setup(string heroName, FP damage, FP teamTotalDamage, Color heroColor)
    {
        if (heroNameText != null)
            heroNameText.text = heroName;

        if (damageText != null)
            damageText.text = FormatCompact(damage.AsFloat);

        float percentage = teamTotalDamage > FP._0 ? (damage / teamTotalDamage).AsFloat * 100f : 0f;

        if (percentageText != null)
            percentageText.text = $"{percentage:0}%";

        if (damageSlider != null)
        {
            damageSlider.maxValue = teamTotalDamage.AsFloat;
            damageSlider.value = damage.AsFloat;
        }

        if (sliderFillImage != null)
            sliderFillImage.color = heroColor;
    }

    // 21203 -> "21.2k", 900 -> "900", 1500000 -> "1.5M" - always one decimal from 1k up, no
    // decimal under 1k. Public/static since RunResultPopup's own common stats (Damage Dealt, Rift
    // Shards Earned) use the exact same formatting.
    public static string FormatCompact(float value)
    {
        if (value >= 1_000_000f)
            return $"{value / 1_000_000f:0.0}M";

        if (value >= 1000f)
            return $"{value / 1000f:0.0}k";

        return Mathf.RoundToInt(value).ToString();
    }
}
