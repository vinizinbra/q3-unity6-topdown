using UnityEngine;
using UnityEngine.UI;

public static class ScrollRectUtility
{
    /// <summary>
    /// Scrolls so <paramref name="item"/> (plus <paramref name="margin"/>, e.g. for a focus glow drawn outside it)
    /// is inside the viewport of a vertical ScrollRect. Gamepad navigation past the visible rows uses this.
    /// </summary>
    public static void EnsureVisible(ScrollRect scroll, RectTransform item, float margin = 0f)
    {
        if (scroll == null || scroll.viewport == null || scroll.content == null || item == null)
            return;

        Canvas.ForceUpdateCanvases();

        RectTransform viewport = scroll.viewport;
        var corners = new Vector3[4];
        item.GetWorldCorners(corners);
        float bottom = viewport.InverseTransformPoint(corners[0]).y - margin;
        float top = viewport.InverseTransformPoint(corners[1]).y + margin;

        Rect view = viewport.rect;
        Vector2 position = scroll.content.anchoredPosition;
        if (top > view.yMax)
            position.y -= top - view.yMax;
        else if (bottom < view.yMin)
            position.y += view.yMin - bottom;
        else
            return;

        scroll.content.anchoredPosition = position;
    }
}
