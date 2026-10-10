using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A titled group of Catalog tiles (a faction, a rarity...): colored header with a discovered count, and the tile grid.</summary>
public class CatalogSectionWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Hidden for a section with no title (the Bosses page lists its tiles without headers).")] private GameObject header;
    [SerializeField] private Image accent;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Transform gridRoot;

    public Transform GridRoot => gridRoot;

    public void Bind(string title, Color accentColor)
    {
        titleText.text = title.ToUpperInvariant();
        accent.color = accentColor;
        if (header != null)
            header.SetActive(!string.IsNullOrEmpty(title));
    }

    public void SetTitle(string title)
    {
        titleText.text = title.ToUpperInvariant();
    }

    public void SetCount(int discovered, int total)
    {
        countText.text = discovered + " / " + total;
    }
}
