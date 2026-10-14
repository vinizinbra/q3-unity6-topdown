using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One line of the Ascensions codex: number, icon, name and one pip per level, filled for each level discovered. Locked
/// lines show a padlock and the requirement instead of the pips; Undiscovered ones (no level found yet) show ???.
/// Click or focus selects it.
/// </summary>
[RequireComponent(typeof(Button))]
public class AscensionRowWidget : MonoBehaviour, ISelectHandler
{
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private Image icon;
    [SerializeField, Tooltip("Shown instead of the icon while Undiscovered.")] private GameObject unknownIcon;
    [SerializeField, Tooltip("Shown instead of the icon while Locked.")] private GameObject lockedIcon;
    [SerializeField] private TMP_Text nameText;

    [Header("Rank pips (one per rank, extra ones hidden)")]
    [SerializeField] private Image[] pips;
    [SerializeField, Tooltip("One padlock centred where the pips are, shown while Locked.")] private GameObject pipsLocked;
    [SerializeField] private Color emptyPipColor = new Color(0.8f, 0.82f, 0.86f);

    [SerializeField, Tooltip("Graphics coloured with the line's kind colour (Skill / Passive / Dash), e.g. the selected-row border.")]
    private Graphic[] kindTinted;

    [SerializeField, Tooltip("What changes while this is the selected line.")]
    private SelectionStyle selectionStyle = new SelectionStyle();

    public event Action<AscensionRowWidget> Clicked;

    public AscensionLine Line { get; private set; }
    public AscensionState State { get; private set; }
    public int Number { get; private set; }
    public int DiscoveredLevels { get; private set; }
    public string LockedRequirement { get; private set; }

    private void Awake() => GetComponent<Button>().onClick.AddListener(RaiseClicked);

    public void Bind(int number, AscensionLine line, AscensionState state, int discoveredLevels, string lockedRequirement, Color kindColor)
    {
        Number = number;
        Line = line;
        State = state;
        DiscoveredLevels = discoveredLevels;
        LockedRequirement = lockedRequirement;

        numberText.text = number.ToString("00");
        if (kindTinted != null)
        {
            foreach (Graphic graphic in kindTinted)
            {
                if (graphic != null)
                    graphic.color = kindColor;
            }
        }

        bool discovered = state == AscensionState.Discovered;
        icon.gameObject.SetActive(discovered);
        if (discovered)
            icon.sprite = line.Upgrade.Icon;
        if (unknownIcon != null)
            unknownIcon.SetActive(state == AscensionState.Undiscovered);
        if (lockedIcon != null)
            lockedIcon.SetActive(state == AscensionState.Locked);

        nameText.text = state switch
        {
            AscensionState.Discovered => line.Name,
            AscensionState.Locked => string.IsNullOrEmpty(lockedRequirement) ? "Locked" : lockedRequirement,
            _ => "???"
        };

        int ranks = line.MaxRank;
        bool locked = state == AscensionState.Locked;
        for (int i = 0; i < pips.Length; i++)
        {
            bool used = i < ranks && !locked;
            pips[i].gameObject.SetActive(used);
            if (used)
                pips[i].color = i < discoveredLevels ? kindColor : emptyPipColor;
        }

        if (pipsLocked != null)
            pipsLocked.SetActive(locked);
    }

    public void SetSelected(bool selected) => selectionStyle.Apply(selected);

    public void OnSelect(BaseEventData eventData) => RaiseClicked();

    private void RaiseClicked() => Clicked?.Invoke(this);

}
