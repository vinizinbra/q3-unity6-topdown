using TMPro;
using UnityEngine;

// Small hover panel (title + description) shown next to whatever UpgradeTooltipTriggerWidget the mouse
// is over - first used by HeroInfoPopupWidget's Rift Mutation icon row. Lives inside the window it
// serves (last sibling, so it draws on top) and sizes itself to its text via its own layout group +
// ContentSizeFitter. Placed below the hovered element and clamped inside `bounds`.
public class UpgradeTooltipWidget : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField, Tooltip("The area the panel must stay inside - typically the window root. Unassigned = no clamping.")]
    private RectTransform bounds;
    [SerializeField] private Vector2 offset = new Vector2(0f, -8f);

    private void Awake()
    {
        Hide();
    }

    public void Show(string title, string description, RectTransform target)
    {
        if (panel == null)
            return;

        if (titleText != null)
            titleText.text = title;

        if (descriptionText != null)
            descriptionText.text = description;

        panel.gameObject.SetActive(true);
        panel.SetAsLastSibling();

        // Sizes come from the ContentSizeFitter, so rebuild now - otherwise the clamp below uses last
        // frame's size and a longer description pokes out of the window for one frame.
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        RectTransform parent = (RectTransform)panel.parent;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);

        // Bottom-left corner of the hovered element, in the panel's parent space.
        Vector2 anchor = parent.InverseTransformPoint(corners[0]);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = anchor + offset;

        Clamp(parent);
    }

    public void Hide()
    {
        if (panel != null && panel.gameObject.activeSelf)
            panel.gameObject.SetActive(false);
    }

    private void Clamp(RectTransform parent)
    {
        if (bounds == null)
            return;

        var corners = new Vector3[4];
        bounds.GetWorldCorners(corners);
        Vector2 min = parent.InverseTransformPoint(corners[0]);
        Vector2 max = parent.InverseTransformPoint(corners[2]);

        Vector2 size = panel.rect.size;
        Vector2 position = panel.anchoredPosition;
        position.x = Mathf.Clamp(position.x, min.x, max.x - size.x);
        position.y = Mathf.Clamp(position.y, min.y + size.y, max.y);
        panel.anchoredPosition = position;
    }
}
