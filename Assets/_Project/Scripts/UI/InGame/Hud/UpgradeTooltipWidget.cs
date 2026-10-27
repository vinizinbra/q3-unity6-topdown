using TMPro;
using UnityEngine;

// Small hover panel (title + description) shown next to whatever UpgradeTooltipTriggerWidget the mouse
// is over - first used by HeroInfoPopupWidget's Rift Mutation icon row. Lives inside the window it
// serves (last sibling, so it draws on top) and sizes itself to its text via its own layout group +
// ContentSizeFitter. Placed below the hovered element - above it when there's no room below (bottom
// rows) - and clamped inside `bounds`.
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
        Vector2 below = (Vector2)parent.InverseTransformPoint(corners[0]) + offset;
        panel.pivot = new Vector2(0f, 1f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = below;

        // Not enough room under it: hang it off the element's top-left corner instead.
        if (bounds != null && below.y - panel.rect.height < BoundsMin(parent).y)
        {
            Vector2 above = (Vector2)parent.InverseTransformPoint(corners[1]) - offset;
            panel.pivot = new Vector2(0f, 0f);
            panel.anchoredPosition = above;
        }

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

        // Works on the panel's bottom-left corner, so it holds for either pivot Show picked.
        Vector2 size = panel.rect.size;
        Vector2 offsetToCorner = Vector2.Scale(panel.pivot, size);
        Vector2 corner = panel.anchoredPosition - offsetToCorner;
        corner.x = Mathf.Clamp(corner.x, min.x, max.x - size.x);
        corner.y = Mathf.Clamp(corner.y, min.y, max.y - size.y);
        panel.anchoredPosition = corner + offsetToCorner;
    }

    private Vector2 BoundsMin(RectTransform parent)
    {
        var corners = new Vector3[4];
        bounds.GetWorldCorners(corners);
        return parent.InverseTransformPoint(corners[0]);
    }
}
