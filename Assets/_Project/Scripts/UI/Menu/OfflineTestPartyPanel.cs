#if CHEATS_ENABLED
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Cheat-build-only menu overlay for OfflineTestParty: a "Bots: N" button plus one hero button per bot
// (tap to cycle). Code-only uGUI, self-instantiating like CheatMenu - no scene object to author. Only
// visible on the main menu once CheatUnlock's corner-tap code has been entered; the next Practice (offline) start picks the party up (MatchMakingConfig.
// AddLocalPlayers).
public class OfflineTestPartyPanel : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject host = new GameObject("OfflineTestPartyPanel");
        host.AddComponent<OfflineTestPartyPanel>();
        DontDestroyOnLoad(host);
    }

    private static readonly Color ButtonBg = new Color(0.38f, 0.4f, 0.48f, 1f);

    private Canvas _canvas;
    private TMP_Text _countLabel;
    private readonly TMP_Text[] _heroLabels = new TMP_Text[OfflineTestParty.MaxBots];
    private readonly GameObject[] _heroButtons = new GameObject[OfflineTestParty.MaxBots];

    private void Awake()
    {
        GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 29000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        canvasGo.AddComponent<GraphicRaycaster>();

        RectTransform column = NewRect(canvasGo.transform, "Column");
        column.anchorMin = column.anchorMax = column.pivot = new Vector2(0, 0);
        column.anchoredPosition = new Vector2(20, 20);
        column.sizeDelta = new Vector2(260, 0);

        VerticalLayoutGroup vlg = column.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Top to bottom, so the heroes stack above the count button nearest the screen corner.
        for (int i = 0; i < OfflineTestParty.MaxBots; i++)
        {
            int bot = i;
            _heroButtons[i] = NewButton(column, () => { OfflineTestParty.CycleHero(bot); Refresh(); }, out _heroLabels[i]);
        }

        NewButton(column, () => { OfflineTestParty.CycleBotCount(); Refresh(); }, out _countLabel);
        Refresh();
    }

    private void Update()
    {
        bool onMainMenu = GameManager.Instance != null
            && GameManager.Instance.MainMenuTab != null
            && GameManager.Instance.MainMenuTab.windowManager != null
            && GameManager.Instance.MainMenuTab.windowManager.currentWindow is MainMenuWindow
            && CheatUnlock.Unlocked;

        if (_canvas.enabled != onMainMenu)
        {
            _canvas.enabled = onMainMenu;
            if (onMainMenu)
                Refresh();
        }
    }

    private void Refresh()
    {
        int count = OfflineTestParty.BotCount;
        _countLabel.text = count == 0 ? "Offline bots: 0" : $"Offline bots: {count}";

        for (int i = 0; i < _heroButtons.Length; i++)
        {
            _heroButtons[i].SetActive(i < count);
            if (i < count)
                _heroLabels[i].text = $"Bot {i + 1}: {OfflineTestParty.HeroLabel(i)}";
        }
    }

    private static RectTransform NewRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static GameObject NewButton(Transform parent, System.Action onClick, out TMP_Text label)
    {
        RectTransform rt = NewRect(parent, "Button");
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = ButtonBg;
        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;

        RectTransform text = NewRect(rt, "Text");
        text.anchorMin = Vector2.zero;
        text.anchorMax = Vector2.one;
        text.offsetMin = text.offsetMax = Vector2.zero;
        label = text.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 22;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        return rt.gameObject;
    }
}
#endif
