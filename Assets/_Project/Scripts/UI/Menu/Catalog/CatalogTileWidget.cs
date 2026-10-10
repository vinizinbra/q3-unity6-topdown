using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One tile of the Catalog grid: icon, number and name. Undiscovered entries show as a black silhouette
/// named "???". Click or gamepad focus selects it (the detail panel follows).
/// </summary>
[RequireComponent(typeof(Button))]
public class CatalogTileWidget : MonoBehaviour, ISelectHandler
{
    [SerializeField] private Image icon;
    [SerializeField, Tooltip("Optional corner check shown once discovered.")] private GameObject discoveredMark;
    [SerializeField, Tooltip("Optional padlock shown while undiscovered.")] private GameObject lockIcon;
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField, Tooltip("Optional small line under the name (the entry's world).")] private TMP_Text worldText;
    [SerializeField, Tooltip("Optional: keeps wide key art filling its frame (Envelope Parent) whatever the sprite's proportions.")] private AspectRatioFitter iconFitter;

    [Header("Number format")]
    [SerializeField] private string numberPrefix = "#";
    [SerializeField] private int numberDigits = 3;

    [Header("Colors")]
    [SerializeField, Tooltip("What changes while this is the selected item: graphic colours (rest / selected) and objects shown only while selected.")]
    private SelectionStyle selectionStyle = new SelectionStyle();
    [SerializeField] private Color silhouetteColor = new Color(0.08f, 0.09f, 0.12f, 0.9f);

    public event Action<CatalogTileWidget> Clicked;

    public CatalogEntryData Entry { get; private set; }
    public int Number { get; private set; }
    public bool Discovered { get; private set; }

    public string NumberLabel => numberPrefix + Number.ToString(new string('0', numberDigits));

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(RaiseClicked);
    }

    public void Bind(CatalogEntryData entry, int number, bool discovered, string worldName = null)
    {
        Entry = entry;
        Number = number;
        Discovered = discovered;

        Sprite sprite = entry.ResolvedIcon;
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        icon.color = discovered ? entry.iconTint : silhouetteColor;
        if (iconFitter != null && sprite != null)
            iconFitter.aspectRatio = sprite.rect.width / sprite.rect.height;
        if (discoveredMark != null)
            discoveredMark.SetActive(discovered);
        if (lockIcon != null)
            lockIcon.SetActive(!discovered);
        if (worldText != null)
        {
            worldText.text = string.IsNullOrEmpty(worldName) ? string.Empty : worldName.ToUpperInvariant();
            worldText.gameObject.SetActive(!string.IsNullOrEmpty(worldName));
        }

        numberText.text = NumberLabel;
        nameText.text = discovered ? entry.ResolvedName.ToUpperInvariant() : "???";
    }

    public void SetSelected(bool selected)
    {
        selectionStyle.Apply(selected);
    }

    public void OnSelect(BaseEventData eventData)
    {
        RaiseClicked();
    }

    private void RaiseClicked()
    {
        Clicked?.Invoke(this);
    }
}
