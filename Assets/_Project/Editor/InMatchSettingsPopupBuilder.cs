using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the InMatchSettingsPopup hierarchy into the GAMEPLAY scene, parented under its
// InMatchPopupManager (which discovers popups via GetComponentsInChildren at Awake, so parenting is
// the whole registration step) and wires every serialized field.
//
// Open the gameplay scene first - InMatchPopupManager is scene-local, it does not exist in
// MenuScene. Re-running is safe: an existing popup is selected instead of duplicated. Everything
// created is plain UGUI meant to be restyled afterwards; nothing here reads back what it authored.
public static class InMatchSettingsPopupBuilder
{
    [MenuItem("Tools/RiftRaiders/UI/Create In-Match Settings Popup")]
    internal static void Create()
    {
        var existing = Object.FindFirstObjectByType<InMatchSettingsPopup>(FindObjectsInactive.Include);

        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.LogWarning("[InMatchSettingsPopup] one already exists in this scene - selected it instead of building a second one.", existing);
            return;
        }

        var manager = Object.FindFirstObjectByType<InMatchPopupManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            Debug.LogError("[InMatchSettingsPopup] no InMatchPopupManager in the open scene - open the gameplay scene and run this again.");
            return;
        }

        var rootGo = new GameObject("InMatchSettingsPopup", typeof(RectTransform), typeof(CanvasGroup), typeof(InMatchSettingsPopup));
        rootGo.transform.SetParent(manager.transform, false);
        Undo.RegisterCreatedObjectUndo(rootGo, "Create In-Match Settings Popup");
        Stretch((RectTransform)rootGo.transform);

        TMP_FontAsset font = ResolveFont();

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(rootGo.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(760f, 640f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.13f, 0.97f);

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 40, 40);
        layout.spacing = 28f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TMP_Text title = CreateText(panel.transform, "Title", "SETTINGS", 52f, font, FontStyles.Bold);
        SetHeight(title.gameObject, 70f);

        Slider sfx = CreateSliderRow(panel.transform, "Sfx", "SFX", font);
        Slider music = CreateSliderRow(panel.transform, "Music", "MUSIC", font);

        Button restart = CreateButton(panel.transform, "RestartButton", "RESTART", font, new Color(0.2f, 0.45f, 0.75f, 1f));
        Button disconnect = CreateButton(panel.transform, "DisconnectButton", "DISCONNECT", font, new Color(0.75f, 0.25f, 0.25f, 1f));
        Button close = CreateButton(panel.transform, "CloseButton", "CLOSE", font, new Color(0.3f, 0.32f, 0.4f, 1f));

        var serialized = new SerializedObject(rootGo.GetComponent<InMatchSettingsPopup>());
        serialized.FindProperty("canvasGroup").objectReferenceValue = rootGo.GetComponent<CanvasGroup>();
        serialized.FindProperty("closeButton").objectReferenceValue = close;
        serialized.FindProperty("sfxSlider").objectReferenceValue = sfx;
        serialized.FindProperty("musicSlider").objectReferenceValue = music;
        serialized.FindProperty("disconnectButton").objectReferenceValue = disconnect;
        serialized.FindProperty("restartButton").objectReferenceValue = restart;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Popups start hidden - InMatchPopupManager activates every one in Awake and hides them all
        // in Start, so this is only about how the scene looks while editing.
        rootGo.SetActive(false);

        Selection.activeGameObject = rootGo;
        EditorSceneManager.MarkSceneDirty(rootGo.scene);

        Debug.Log("[InMatchSettingsPopup] built and wired under InMatchPopupManager. Escape opens/closes it in a match; wire a HUD button's onClick to InMatchPopupManager.OpenSettings for a clickable entry point.", rootGo);
    }

    private static TMP_FontAsset ResolveFont()
    {
        // Same idea as LoadingWindowBuilder: reuse whatever the scene's UI already uses.
        foreach (var text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.font != null)
                return text.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    private static Slider CreateSliderRow(Transform parent, string name, string label, TMP_FontAsset font)
    {
        var row = new GameObject(name + "Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        SetHeight(row, 60f);

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        TMP_Text text = CreateText(row.transform, "Label", label, 34f, font, FontStyles.Normal);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.gameObject.AddComponent<LayoutElement>().preferredWidth = 180f;

        var sliderGo = new GameObject(name + "Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        sliderGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Image background = CreateImage(sliderGo.transform, "Background", new Color(1f, 1f, 1f, 0.15f));
        background.raycastTarget = true;
        var bgRect = background.rectTransform;
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(1f, 0.5f);
        bgRect.sizeDelta = new Vector2(0f, 16f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.sizeDelta = new Vector2(-20f, 16f);

        Image fill = CreateImage(fillArea.transform, "Fill", new Color(0.4f, 0.85f, 1f, 1f));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        var handleAreaRect = (RectTransform)handleArea.transform;
        Stretch(handleAreaRect);
        handleAreaRect.offsetMin = new Vector2(10f, 0f);
        handleAreaRect.offsetMax = new Vector2(-10f, 0f);

        Image handle = CreateImage(handleArea.transform, "Handle", Color.white);
        handle.raycastTarget = true;
        handle.rectTransform.sizeDelta = new Vector2(32f, 44f);

        var slider = sliderGo.GetComponent<Slider>();
        slider.targetGraphic = handle;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        return slider;
    }

    private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetHeight(go, 72f);

        var image = go.GetComponent<Image>();
        image.color = color;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = CreateText(go.transform, "Label", label, 36f, font, FontStyles.Bold);
        Stretch(text.rectTransform);

        return button;
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

    private static void SetHeight(GameObject go, float height)
    {
        var element = go.GetComponent<LayoutElement>();
        if (element == null)
            element = go.AddComponent<LayoutElement>();

        element.preferredHeight = height;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
