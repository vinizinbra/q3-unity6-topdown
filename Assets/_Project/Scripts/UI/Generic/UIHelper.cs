using UnityEngine;

// Converts a world-space position into the anchored position of a UI element parented somewhere
// under a Canvas, so HUD widgets (health bars, nameplates, etc.) can follow a world Transform
// regardless of whether that Canvas is Screen Space - Overlay, Screen Space - Camera, or World Space.
public static class UIHelper
{
    public static bool TryWorldToAnchoredPosition(RectTransform target, Canvas canvas, Camera worldCamera, Vector3 worldPosition, out Vector2 anchoredPosition)
    {
        anchoredPosition = default;

        var parentRect = target.parent as RectTransform;
        if (parentRect == null || canvas == null || worldCamera == null)
            return false;

        Vector2 screenPoint = worldCamera.WorldToScreenPoint(worldPosition);
        // ScreenPointToLocalPointInRectangle expects null for Overlay canvases and the canvas's
        // own render camera otherwise - passing worldCamera there would be wrong for Overlay mode.
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out anchoredPosition);
    }
}
