using UnityEngine;
using UnityEngine.EventSystems;

// Put on any UI element (its Graphic must be a raycast target) to show an UpgradeTooltipWidget while
// the mouse is over it. Plain data-in: whoever fills the element calls Setup with the text.
public class UpgradeTooltipTriggerWidget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private UpgradeTooltipWidget tooltip;

    private string _title;
    private string _description;
    private bool _hovered;

    public void Setup(UpgradeTooltipWidget tooltip, string title, string description)
    {
        this.tooltip = tooltip;
        _title = title;
        _description = description;

        // Refilled while hovered (the popup rebuilt under the mouse) - keep the panel in sync.
        if (_hovered)
            Show();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        Show();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;

        if (tooltip != null)
            tooltip.Hide();
    }

    // The icon can be hidden/recycled while hovered (popup closed, mutation list rebuilt) - no exit
    // event comes then, so the panel would otherwise stay up.
    private void OnDisable()
    {
        if (_hovered && tooltip != null)
            tooltip.Hide();

        _hovered = false;
    }

    private void Show()
    {
        if (tooltip != null && string.IsNullOrEmpty(_title) == false)
            tooltip.Show(_title, _description, (RectTransform)transform);
    }
}
