using System.Collections.Generic;
using QuantumUser.View.Util;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Shadow = UnityEngine.UI.Shadow;

/// <summary>
/// One-off fix for a Canvas whose CanvasScaler Reference Resolution was authored swapped
/// (e.g. 1080x1920 instead of 1920x1080). Swapping the Reference Resolution alone changes
/// the CanvasScaler's computed scale factor, which shifts/shrinks every already-authored
/// child (their anchoredPosition/sizeDelta/offsets and TMP/Text font sizes are all expressed
/// in the old, wrong "canvas space" units). This recomputes the old vs. new scale factor with
/// CanvasScaler's own formula and rescales every descendant so the on-screen result is
/// preserved while the Reference Resolution itself gets corrected.
/// </summary>
public static class FixCanvasReferenceResolution
{
    private const string MenuPath = "Tools/RiftRaiders/Utilities/Fix Canvas Reference Resolution (Swap X/Y)";

    [MenuItem(MenuPath, false, 200)]
    private static void Fix()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("Fix Canvas Reference Resolution",
                "Select the Canvas GameObject (the one with the CanvasScaler component) first.", "OK");
            return;
        }

        var scaler = go.GetComponent<CanvasScaler>();
        var canvas = go.GetComponent<Canvas>();
        if (scaler == null || canvas == null)
        {
            EditorUtility.DisplayDialog("Fix Canvas Reference Resolution",
                "Selected object needs both a Canvas and a CanvasScaler component.", "OK");
            return;
        }

        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            EditorUtility.DisplayDialog("Fix Canvas Reference Resolution",
                "This CanvasScaler isn't using 'Scale With Screen Size' — nothing to recompute.", "OK");
            return;
        }

        var oldRes = scaler.referenceResolution;
        var newRes = new Vector2(oldRes.y, oldRes.x);

        if (!EditorUtility.DisplayDialog("Fix Canvas Reference Resolution",
                $"Reference Resolution on '{go.name}' will change from {oldRes} to {newRes}.\n\n" +
                "Every descendant RectTransform (offsets), TMP/legacy Text font sizes and common " +
                "LayoutElement/Layout Group pixel fields will be rescaled to preserve how the UI " +
                "currently looks. This is undoable in one Ctrl+Z.\n\nProceed?", "Fix", "Cancel"))
        {
            return;
        }

        // Assumed real target device resolution the game actually renders at. Only matters when
        // ScreenMatchMode blends width/height (MatchWidthOrHeight between 0 and 1) with a non-16:9
        // aspect; for pure width- or height-match modes the result is independent of this value.
        var assumedScreen = new Vector2(1920, 1080);

        float oldScaleFactor = ComputeScaleFactor(scaler, oldRes, assumedScreen);
        float newScaleFactor = ComputeScaleFactor(scaler, newRes, assumedScreen);
        float ratio = oldScaleFactor / newScaleFactor;

        Undo.SetCurrentGroupName("Fix Canvas Reference Resolution");
        int undoGroup = Undo.GetCurrentGroup();

        Undo.RecordObject(scaler, "Fix Canvas Reference Resolution");
        scaler.referenceResolution = newRes;

        int rectCount = 0, textCount = 0, layoutCount = 0, imageCount = 0;
        RescaleChildren(go.transform, ratio, ref rectCount, ref textCount, ref layoutCount, ref imageCount);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(go.scene);

        LogHelper.Log("UI",
            $"Fixed CanvasScaler on '{go.name}': referenceResolution {oldRes} -> {newRes}, " +
            $"rescale ratio {ratio:0.####}. Rescaled {rectCount} RectTransforms, {textCount} text " +
            $"components, {layoutCount} layout components, {imageCount} image/shadow components.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateFix()
    {
        var go = Selection.activeGameObject;
        return go != null && go.GetComponent<CanvasScaler>() != null && go.GetComponent<Canvas>() != null;
    }

    private static float ComputeScaleFactor(CanvasScaler scaler, Vector2 referenceResolution, Vector2 screenSize)
    {
        switch (scaler.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.Expand:
                return Mathf.Min(screenSize.x / referenceResolution.x, screenSize.y / referenceResolution.y);
            case CanvasScaler.ScreenMatchMode.Shrink:
                return Mathf.Max(screenSize.x / referenceResolution.x, screenSize.y / referenceResolution.y);
            default: // MatchWidthOrHeight
                float logWidth = Mathf.Log(screenSize.x / referenceResolution.x, 2);
                float logHeight = Mathf.Log(screenSize.y / referenceResolution.y, 2);
                float logWeightedAverage = Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight);
                return Mathf.Pow(2, logWeightedAverage);
        }
    }

    private static void RescaleChildren(Transform root, float ratio, ref int rectCount, ref int textCount, ref int layoutCount, ref int imageCount)
    {
        var stack = new Stack<Transform>();
        for (int i = 0; i < root.childCount; i++)
            stack.Push(root.GetChild(i));

        while (stack.Count > 0)
        {
            var t = stack.Pop();

            // Nested Canvas with its own CanvasScaler manages its own space; skip its subtree.
            var nestedScaler = t.GetComponent<CanvasScaler>();
            if (nestedScaler != null && t.GetComponent<Canvas>() != null)
                continue;

            if (t.TryGetComponent<RectTransform>(out var rect))
            {
                // anchoredPosition/sizeDelta are the raw serialized fields (independent of the
                // parent's current live rect). offsetMin/offsetMax are computed *from* those plus
                // the parent's rect at the time of access, which is unsafe to rely on here: the
                // Canvas's own rect only catches up to the new referenceResolution on its next
                // CanvasScaler.Update() tick, not synchronously when this script sets it. Using the
                // derived offsets earlier caused inconsistent results depending on hierarchy depth.
                Undo.RecordObject(rect, "Fix Canvas Reference Resolution");
                rect.anchoredPosition *= ratio;
                rect.sizeDelta *= ratio;
                rectCount++;
            }

            if (t.TryGetComponent<TMP_Text>(out var tmp))
            {
                Undo.RecordObject(tmp, "Fix Canvas Reference Resolution");
                tmp.fontSize *= ratio;
                tmp.fontSizeMin *= ratio;
                tmp.fontSizeMax *= ratio;
                textCount++;
            }
            else if (t.TryGetComponent<Text>(out var legacyText))
            {
                Undo.RecordObject(legacyText, "Fix Canvas Reference Resolution");
                legacyText.fontSize = Mathf.Max(1, Mathf.RoundToInt(legacyText.fontSize * ratio));
                textCount++;
            }

            if (t.TryGetComponent<Image>(out var image) &&
                (image.type == Image.Type.Sliced || image.type == Image.Type.Tiled))
            {
                // Slice/tile border thickness on screen is inversely proportional to this
                // multiplier. The box itself just shrank/grew by `ratio`; scale the multiplier by
                // 1/ratio so the border stays the same fraction of the box instead of looking
                // disproportionately thick or thin.
                Undo.RecordObject(image, "Fix Canvas Reference Resolution");
                image.pixelsPerUnitMultiplier /= ratio;
                imageCount++;
            }

            if (t.TryGetComponent<Shadow>(out var shadow))
            {
                Undo.RecordObject(shadow, "Fix Canvas Reference Resolution");
                shadow.effectDistance *= ratio;
                imageCount++;
            }

            if (t.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                Undo.RecordObject(layoutElement, "Fix Canvas Reference Resolution");
                if (layoutElement.minWidth > 0) layoutElement.minWidth *= ratio;
                if (layoutElement.minHeight > 0) layoutElement.minHeight *= ratio;
                if (layoutElement.preferredWidth > 0) layoutElement.preferredWidth *= ratio;
                if (layoutElement.preferredHeight > 0) layoutElement.preferredHeight *= ratio;
                layoutCount++;
            }

            if (t.TryGetComponent<HorizontalOrVerticalLayoutGroup>(out var hvLayout))
            {
                Undo.RecordObject(hvLayout, "Fix Canvas Reference Resolution");
                hvLayout.spacing *= ratio;
                var padding = hvLayout.padding;
                padding.left = Mathf.RoundToInt(padding.left * ratio);
                padding.right = Mathf.RoundToInt(padding.right * ratio);
                padding.top = Mathf.RoundToInt(padding.top * ratio);
                padding.bottom = Mathf.RoundToInt(padding.bottom * ratio);
                hvLayout.padding = padding;
                layoutCount++;
            }

            if (t.TryGetComponent<GridLayoutGroup>(out var gridLayout))
            {
                Undo.RecordObject(gridLayout, "Fix Canvas Reference Resolution");
                gridLayout.cellSize *= ratio;
                gridLayout.spacing *= ratio;
                var padding = gridLayout.padding;
                padding.left = Mathf.RoundToInt(padding.left * ratio);
                padding.right = Mathf.RoundToInt(padding.right * ratio);
                padding.top = Mathf.RoundToInt(padding.top * ratio);
                padding.bottom = Mathf.RoundToInt(padding.bottom * ratio);
                gridLayout.padding = padding;
                layoutCount++;
            }

            for (int i = 0; i < t.childCount; i++)
                stack.Push(t.GetChild(i));
        }
    }
}
