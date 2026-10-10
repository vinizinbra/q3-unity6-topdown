using System;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Row of tab buttons, each showing its own page (one visible at a time). Used by the Heroes and Loadout tabs.</summary>
public class TabStripWidget : MonoBehaviour
{
    [Serializable]
    private class Entry
    {
        public Button button;
        public Image background;
        public TMP_Text label;
        public GameObject page;
    }

    [SerializeField] private Entry[] tabs;
    [SerializeField] private int startIndex;

    [Header("Folder-tab look")]
    [SerializeField, Tooltip("Extra px an inactive neighbour reaches under the selected tab, on top of what the chamfers need.")]
    private float extraOverlap = 16f;
    [SerializeField, Tooltip("Inactive tabs are this many px shorter than the selected one, which stands above them and is drawn on top.")]
    private float inactiveDrop = 10f;
    [SerializeField, Tooltip("Pixels-per-unit multiplier of the tab sprite: inactive tabs use this higher value (smaller corners/border), the selected one uses Active.")]
    private float inactivePixelsMultiplier = 3f;
    [SerializeField] private float activePixelsMultiplier = 1f;

    [Header("Switch animation")]
    [SerializeField, Tooltip("Seconds the tabs take to change state (the new tab rises, the old one sinks).")]
    private float switchDuration = 0.28f;
    [SerializeField, Tooltip("Rise overshoot of the tab that becomes selected.")]
    private Ease riseEase = Ease.OutBack;
    [SerializeField, Tooltip("Pop of the selected tab's label, as a scale fraction. 0 = none.")]
    private float labelPop = 0.25f;

    [Header("Colors")]
    [SerializeField] private Color activeBackground = Color.white;
    [SerializeField] private Color idleBackground = new Color(0.80f, 0.82f, 0.87f);
    [SerializeField] private Color activeLabel = new Color(0.10f, 0.11f, 0.15f);
    [SerializeField] private Color idleLabel = new Color(0.45f, 0.47f, 0.53f);

    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Raised whenever a tab becomes the selected one.</summary>
    public event Action<int> Changed;

    private void Awake()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabs[i].button.onClick.AddListener(() => Select(index));
        }
    }

    private void Start()
    {
        Select(startIndex);
    }

    private void AlignNeighbours(int selected)
    {
        Sprite sprite = tabs[selected].background.sprite;
        float border = sprite != null ? sprite.border.x : 0f;

        // Chamfer width in canvas units for the selected (big) and a neighbour (small) tab, minus the height the
        // neighbour sits lower, plus its own corner cut.
        float reach = Mathf.Max(0f, border / activePixelsMultiplier - inactiveDrop + border / inactivePixelsMultiplier) + extraOverlap;

        for (int i = 0; i < tabs.Length; i++)
        {
            var rect = (RectTransform)tabs[i].button.transform;
            float left = i == selected + 1 ? -reach : 0f;
            float right = i == selected - 1 ? reach : 0f;
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(right, rect.offsetMax.y);
        }
    }

    /// <summary>Moves to the next/previous tab, wrapping around. False when there is nothing to switch to.</summary>
    public bool Step(int direction)
    {
        if (tabs == null || tabs.Length < 2)
            return false;

        Select(((SelectedIndex + direction) % tabs.Length + tabs.Length) % tabs.Length);
        return true;
    }

    /// <summary>The button of the selected tab (a focus target that never gets hidden with a page).</summary>
    public Selectable SelectedButton => SelectedIndex >= 0 && SelectedIndex < tabs.Length ? tabs[SelectedIndex].button : null;

    public void Select(int index)
    {
        if (index < 0 || index >= tabs.Length)
            return;

        int previous = SelectedIndex;
        SelectedIndex = index;
        bool animate = previous >= 0 && previous != index && isActiveAndEnabled;

        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = i == index;
            if (tabs[i].page != null)
            {
                tabs[i].page.SetActive(active);

                // The page that comes in rises and fades in (only on a real switch, not when the screen first opens).
                if (animate && active)
                    PageTransitionWidget.Play(tabs[i].page);
            }

            if (animate && (i == index || i == previous))
                AnimateTab(tabs[i], active);
            else
                SnapTab(tabs[i], active);
        }

        // The selected tab is drawn over its neighbours, and each neighbour reaches under it just far enough that the
        // selected tab's diagonal edge meets the neighbour's own (smaller) chamfer: the strip reads as one curve.
        tabs[index].button.transform.SetAsLastSibling();
        AlignNeighbours(index);

        Changed?.Invoke(index);
    }

    private void SnapTab(Entry tab, bool active)
    {
        Tween.StopAll(tab);
        Tween.StopAll(tab.background);
        tab.background.color = active ? activeBackground : idleBackground;
        tab.label.color = active ? activeLabel : idleLabel;
        SetRise(tab, active ? 1f : 0f);
    }

    // 0 = sunk (inactive: shorter, bigger-PPU sprite), 1 = raised (selected). Rise may overshoot above 1 (OutBack).
    private void SetRise(Entry tab, float rise)
    {
        var rect = (RectTransform)tab.button.transform;
        rect.offsetMax = new Vector2(rect.offsetMax.x, -inactiveDrop * (1f - rise));
        tab.background.pixelsPerUnitMultiplier = Mathf.Lerp(inactivePixelsMultiplier, activePixelsMultiplier, Mathf.Clamp01(rise));
    }

    private void AnimateTab(Entry tab, bool active)
    {
        Tween.StopAll(tab);
        Tween.StopAll(tab.background);

        float from = (-((RectTransform)tab.button.transform).offsetMax.y) / Mathf.Max(0.0001f, inactiveDrop);
        from = 1f - Mathf.Clamp01(from);
        Tween.Custom(tab, from, active ? 1f : 0f, switchDuration, (t, v) => SetRise(t, v), active ? riseEase : Ease.OutCubic, useUnscaledTime: true);
        Tween.Color(tab.background, active ? activeBackground : idleBackground, switchDuration * 0.7f, useUnscaledTime: true);
        tab.label.color = active ? activeLabel : idleLabel;

        if (active && labelPop > 0f)
        {
            Tween.StopAll(tab.label.transform);
            tab.label.transform.localScale = Vector3.one;
            Tween.PunchScale(tab.label.transform, new Vector3(labelPop, labelPop, 0f), 0.35f, useUnscaledTime: true);
        }
    }
}
