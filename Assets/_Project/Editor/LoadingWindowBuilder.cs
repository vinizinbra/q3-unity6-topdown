using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the LoadingWindow hierarchy into the MENU scene, parented under MainMenuTab's own
// WindowManager (which discovers its windows via GetComponentsInChildren at Awake, so parenting is
// the whole registration step - there's no list to also remember to update), and wires every
// serialized field.
//
// The window gets its OWN nested Canvas with Override Sorting on: the menu canvases sort at 0 while
// the gameplay HUD's canvas sorts at 11, so without it the HUD of a match that hasn't visually
// started yet would draw on top of the loading screen meant to be hiding it. No CanvasScaler - a
// nested canvas inherits its parent's scale, which is exactly what keeps this authored against the
// same reference resolution as every other menu window.
//
// A builder rather than a prefab because there's exactly one instance and it needs that canvas
// override, which is scene setup rather than reusable content - and because an unwired field here
// means the player watches an empty level generate, so "author it by hand and don't forget one" is a
// worse failure mode than usual.
//
// Re-running is safe: an existing LoadingWindow is selected and left alone rather than duplicated.
// Delete it first if you want a fresh build. Everything it creates is plain UGUI, so restyling it
// afterwards is expected - nothing here reads back what it authored.
public static class LoadingWindowBuilder
{
    private const int SortingOrder = 999;

    [MenuItem("Tools/RiftRaiders/Create Loading Window")]
    internal static void Create()
    {
        var existing = Object.FindFirstObjectByType<LoadingWindow>(FindObjectsInactive.Include);

        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.LogWarning("[LoadingWindow] one already exists in this scene - selected it instead of building a second one.", existing);
            return;
        }

        WindowManager windowManager = ResolveWindowManager();

        if (windowManager == null)
        {
            Debug.LogError("[LoadingWindow] no WindowManager found in the open scene - open MenuScene (the one with MainMenuTab) and run this again.");
            return;
        }

        var rootGo = new GameObject("LoadingWindow", typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(LoadingWindow));
        rootGo.transform.SetParent(windowManager.transform, false);
        Undo.RegisterCreatedObjectUndo(rootGo, "Create Loading Window");

        Stretch((RectTransform)rootGo.transform);

        var canvas = rootGo.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;

        TMP_FontAsset font = ResolveFont();

        // Opaque backdrop, raycast target on - together with the CanvasGroup's blocksRaycasts this is
        // what keeps clicks off whatever is sitting underneath while the screen is up.
        Image backdrop = CreateImage(rootGo.transform, "Backdrop", new Color(0.03f, 0.03f, 0.05f, 1f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;

        TMP_Text title = CreateText(rootGo.transform, "Title", "RIFT RAIDERS", 88f, font, FontStyles.Bold);
        Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1200f, 140f));

        TMP_Text status = CreateText(rootGo.transform, "StatusText", "CONNECTING", 44f, font, FontStyles.Normal);
        Anchor(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1200f, 70f));

        Slider slider = CreateProgressBar(rootGo.transform);
        Anchor(slider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(900f, 26f));

        TMP_Text percent = CreateText(rootGo.transform, "PercentText", "0%", 32f, font, FontStyles.Normal);
        Anchor(percent.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(400f, 50f));

        TMP_Text tip = CreateText(rootGo.transform, "TipText", string.Empty, 28f, font, FontStyles.Italic);
        Anchor(tip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1400f, 60f));
        tip.color = new Color(1f, 1f, 1f, 0.6f);

        Wire(rootGo.GetComponent<LoadingWindow>(), rootGo.GetComponent<CanvasGroup>(), status, percent, slider, tip);

        // Windows in this scene start hidden - WindowManager shows the right one itself (and its
        // ForceAwakeOnInactiveWindows still initializes this one either way).
        rootGo.SetActive(false);

        Selection.activeGameObject = rootGo;
        EditorSceneManager.MarkSceneDirty(rootGo.scene);

        Debug.Log("[LoadingWindow] built and wired under the menu's WindowManager. MatchMakingConfig.StartRunner shows it, and it hands off to InMatchWindow once the local hero spawns - restyle it freely. Two arrays are left for you: drag the menu background object(s) into Fade With Screen (otherwise the fade reveals the menu instead of the game), and drop hint lines into Tips.", rootGo);
    }

    private static WindowManager ResolveWindowManager()
    {
        // MainMenuTab's manager specifically - that's the one every window transition in the
        // matchmaking flow (ConnectingWindow, InMatchWindow) already goes through.
        var mainMenuTab = Object.FindFirstObjectByType<MainMenuTab>(FindObjectsInactive.Include);

        if (mainMenuTab != null && mainMenuTab.windowManager != null)
            return mainMenuTab.windowManager;

        return Object.FindFirstObjectByType<WindowManager>(FindObjectsInactive.Include);
    }

    private static void Wire(LoadingWindow window, CanvasGroup canvasGroup,
        TMP_Text status, TMP_Text percent, Slider slider, TMP_Text tip)
    {
        var serialized = new SerializedObject(window);
        serialized.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
        serialized.FindProperty("statusText").objectReferenceValue = status;
        serialized.FindProperty("percentText").objectReferenceValue = percent;
        serialized.FindProperty("progressSlider").objectReferenceValue = slider;
        serialized.FindProperty("tipText").objectReferenceValue = tip;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TMP_FontAsset ResolveFont()
    {
        // Prefer whatever the scene's own UI already uses, so this matches the game's type rather
        // than TMP's Liberation Sans default.
        foreach (var text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.font != null)
                return text.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    private static Slider CreateProgressBar(Transform parent)
    {
        var sliderGo = new GameObject("ProgressBar", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);

        Image background = CreateImage(sliderGo.transform, "Background", new Color(1f, 1f, 1f, 0.12f));
        Stretch(background.rectTransform);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        Stretch((RectTransform)fillArea.transform);

        Image fill = CreateImage(fillArea.transform, "Fill", new Color(0.4f, 0.85f, 1f, 1f));
        Stretch(fill.rectTransform);

        var slider = sliderGo.GetComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;
        slider.targetGraphic = background;
        slider.fillRect = fill.rectTransform;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;

        return slider;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        return image;
    }

    private static TMP_Text CreateText(Transform parent, string name, string content, float size, TMP_FontAsset font, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.color = Color.white;

        if (font != null)
            text.font = font;

        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }
}
