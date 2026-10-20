using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Reports a party slot's real height to the PartyBar's VerticalLayoutGroup: the card itself, plus
// the icon row hanging below it (the UpgradeHistory grid) only while that row has icons. Replaces a
// fixed bar spacing sized for a full row, which left a dead gap under player 1 early in a run.
// Outranks the slot's own Image via layoutPriority, so the card sprite's native size never leaks in.
// Children must be anchored to the slot's top - the bar resizes the slot to this height.
[RequireComponent(typeof(RectTransform))]
public class PartySlotHeightWidget : UIBehaviour, ILayoutElement
{
    [SerializeField, Tooltip("Height of the card alone, with no row below it.")]
    private float baseHeight = 132f;
    [SerializeField, Tooltip("Row hanging below the card - counted only while it has an active child.")]
    private RectTransform extraRow;
    [SerializeField, Tooltip("Space kept under the extra row when it shows.")]
    private float bottomPadding = 4f;

    private float _preferredHeight;

    public float minWidth => -1f;
    public float preferredWidth => -1f;
    public float flexibleWidth => -1f;
    public float minHeight => -1f;
    public float preferredHeight => _preferredHeight;
    public float flexibleHeight => -1f;
    public int layoutPriority => 1;

    public void CalculateLayoutInputHorizontal()
    {
    }

    public void CalculateLayoutInputVertical()
    {
        _preferredHeight = baseHeight;

        if (extraRow == null || extraRow.gameObject.activeInHierarchy == false || HasActiveChild(extraRow) == false)
            return;

        // Offset of the row's top edge below the slot's top edge, in slot space.
        var self = (RectTransform)transform;
        var corners = new Vector3[4];
        extraRow.GetWorldCorners(corners);
        float rowTop = self.InverseTransformPoint(corners[1]).y;
        float topOffset = self.rect.yMax - rowTop;

        float rowHeight = LayoutUtility.GetPreferredHeight(extraRow);
        _preferredHeight = Mathf.Max(baseHeight, topOffset + rowHeight + bottomPadding);
    }

    private static bool HasActiveChild(Transform row)
    {
        for (int i = 0; i < row.childCount; i++)
        {
            if (row.GetChild(i).gameObject.activeSelf)
                return true;
        }

        return false;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
    }

    protected override void OnDisable()
    {
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        base.OnDisable();
    }
}
