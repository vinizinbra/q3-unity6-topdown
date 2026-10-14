using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A titled group of the Ascensions codex (SKILL / PASSIVE / DASH): a light header strip in the group's colour with a
/// solid label and a discovered count, then the rows.
/// </summary>
public class AscensionSectionWidget : MonoBehaviour
{
    [SerializeField, Tooltip("The header strip - a light tint of the section colour.")] private Image headerBackground;
    [SerializeField, Tooltip("The solid label box behind the title.")] private Image labelBackground;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Transform rowsRoot;
    [SerializeField, Range(0f, 1f)] private float headerTint = 0.15f;

    public Transform RowsRoot => rowsRoot;

    public void Bind(string title, Color color)
    {
        titleText.text = title.ToUpperInvariant();
        if (headerBackground != null)
            headerBackground.color = Color.Lerp(Color.white, color, headerTint);
        if (labelBackground != null)
            labelBackground.color = color;
        if (countText != null)
            countText.color = color;
    }

    public void SetCount(int discovered, int total)
    {
        if (countText != null)
            countText.text = $"({discovered} / {total} DISCOVERED)";
    }
}
