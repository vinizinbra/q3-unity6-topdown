using System.Collections.Generic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Mini ping chat in the gameplay HUD (see docs/ping.md): the last few PingPlaced events as short
// "Name: message" lines that fade out. Builds its own uGUI at runtime so it needs no scene/prefab
// authoring - GameplayUiController.QStart calls Ensure(). Also the single place that turns a
// PingKind into text and a player into a colour, so the minimap radar reuses PingPresentation.
public class PingFeedWidget : MonoBehaviour
{
    private const int MaxLines = 5;
    private const float LineLifetime = 8f;
    private const float FadeTime = 1f;

    private struct Line
    {
        public TMP_Text Text;
        public float ExpiresAt;
    }

    private readonly List<Line> _lines = new List<Line>();
    private RectTransform _root;

    public static PingFeedWidget Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        _audio = null;
        _audioLoaded = false;
        PingPresentation.ResetSprites();
    }

    // Parents under the first Canvas found beside the caller (the gameplay HUD canvas).
    public static void Ensure(Component anchor)
    {
        if (Instance != null)
            return;

        Canvas canvas = anchor.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        var go = new GameObject("PingFeedWidget", typeof(RectTransform));
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        go.AddComponent<PingFeedWidget>();
    }

    private void Awake()
    {
        Instance = this;

        _root = (RectTransform)transform;
        _root.anchorMin = _root.anchorMax = new Vector2(0f, 0f);
        _root.pivot = new Vector2(0f, 0f);
        _root.anchoredPosition = new Vector2(24f, 220f);
        _root.sizeDelta = new Vector2(620f, 160f);

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 2f;

        QuantumEvent.Subscribe<EventPingPlaced>(this, OnPingPlaced);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnPingPlaced(EventPingPlaced e)
    {
        string nickname = e.Game.Frames.Verified.GetPlayerData(e.Player)?.PlayerNickname;
        if (string.IsNullOrEmpty(nickname))
            nickname = $"Player {(int)e.Player + 1}";

        string hex = ColorUtility.ToHtmlStringRGB(PingPresentation.GetPlayerColor(e.Player));
        AddLine($"<b><color=#{hex}>{nickname}</color></b>  {PingPresentation.GetMessage(e.Kind, e.State)}");
        PlaySound(e.Kind);
        PingWorldRippleWidget.Spawn(e.Player, e.Position.ToUnityVector3(), PingPresentation.GetPlayerColor(e.Player));
    }

    // Resolved lazily from Resources; a missing asset just means no ping sound.
    private static PingAudioData _audio;
    private static bool _audioLoaded;

    private static void PlaySound(PingKind kind)
    {
        if (_audioLoaded == false)
        {
            _audio = Resources.Load<PingAudioData>("PingAudioData");
            _audioLoaded = true;
        }

        SoundData data = _audio != null ? _audio.Get(kind) : null;
        if (data != null)
            AudioManager.Play(data);
    }

    private void AddLine(string rich)
    {
        if (_lines.Count >= MaxLines)
            RemoveLine(0);

        var go = new GameObject("PingLine", typeof(RectTransform));
        go.transform.SetParent(_root, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = rich;
        text.fontSize = 22f;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(0, 0, 0, 255);

        _lines.Add(new Line { Text = text, ExpiresAt = Time.unscaledTime + LineLifetime });
    }

    private void RemoveLine(int index)
    {
        if (_lines[index].Text != null)
            Destroy(_lines[index].Text.gameObject);
        _lines.RemoveAt(index);
    }

    // Unscaled: Level-Up eases Time.timeScale to 0 and the feed should keep ageing out.
    private void Update()
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            float remaining = _lines[i].ExpiresAt - Time.unscaledTime;
            if (remaining <= 0f)
            {
                RemoveLine(i);
                continue;
            }

            _lines[i].Text.alpha = Mathf.Clamp01(remaining / FadeTime);
        }
    }
}

// Shared text + colour for a ping, used by the feed and the minimap radar.
public static class PingPresentation
{
    private static Sprite _ringSprite;
    private static Sprite _dotSprite;

    public static void ResetSprites()
    {
        _ringSprite = null;
        _dotSprite = null;
    }

    // Procedural white ring / dot, built once and shared by the minimap radar and the world ripple.
    public static Sprite GetSprite(bool ring)
    {
        ref Sprite cached = ref (ring ? ref _ringSprite : ref _dotSprite);
        if (cached != null)
            return cached;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = ring
                    ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) / 0.07f)
                    : Mathf.Clamp01((1f - d) / 0.06f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        cached = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cached;
    }

    // Placeholder per-slot palette until heroes have an authored colour.
    private static readonly Color[] PlayerColors =
    {
        new Color(0.30f, 0.80f, 1.00f),
        new Color(1.00f, 0.55f, 0.20f),
        new Color(0.45f, 1.00f, 0.45f),
        new Color(1.00f, 0.35f, 0.75f),
        new Color(1.00f, 0.90f, 0.25f),
        new Color(0.70f, 0.55f, 1.00f),
    };

    public static Color GetPlayerColor(PlayerRef player)
    {
        int index = Mathf.Max(0, (int)player);
        return PlayerColors[index % PlayerColors.Length];
    }

    // Default highlight - the colour the upgrade-card descriptions use.
    private const string Highlight = "#FD3971";

    // Per-POI highlight = the dominant colour of that POI's minimap icon (Assets/0_Refs/PoiIcons.png),
    // so the feed word matches the icon the player sees on the map. Team Challenge has no icon of its
    // own and shares the Challenge icon's orange; Elite shares the Cursed Rift pink.
    private const string StoreColor = "#E7BB05";
    private const string GunsmithColor = "#0BB7E2";
    private const string ShrineColor = "#31C734";
    private const string RiftColor = "#D7135B";
    private const string ChallengeColor = "#DE5A04";

    // Light roast for a failed Team Challenge - picked on the view side, so it never touches the sim.
    private static readonly string[] FailedTrashTalk =
    {
        "failed... smooth, team.",
        "failed. Was it the lag? Sure, the lag.",
        "failed. Bold strategy, let's see if it pays off.",
        "failed. Everybody saw that.",
    };

    private static string H(string word, string hex = Highlight) => $"<color={hex}>{word}</color>";

    // state only matters for POI pings: Available (or anything usable) invites the team over, a
    // PhaseUnavailable POI says when it opens, an AlreadyUsed one says it's spent.
    public static string GetMessage(PingKind kind, ContextInteractionState state = ContextInteractionState.None)
    {
        bool closed = state == ContextInteractionState.PhaseUnavailable;
        bool used = state == ContextInteractionState.AlreadyUsed;

        switch (kind)
        {
            case PingKind.Elite: return $"Focus the {H("Elite", RiftColor)}!";
            case PingKind.ReviveMe: return $"{H("Revive")} me!";
            case PingKind.Store:
                return closed ? $"{H("Store", StoreColor)} here - opens on the next Break"
                    : $"{H("Store", StoreColor)} here - come shop!";
            case PingKind.Blacksmith:
                return closed ? $"{H("Gunsmith", GunsmithColor)} here - opens on the next Break"
                    : used ? $"{H("Gunsmith", GunsmithColor)} here - already used"
                    : $"{H("Gunsmith", GunsmithColor)} here - upgrade your guns!";
            case PingKind.Shrine:
                return closed ? $"{H("Healing Shrine", ShrineColor)} here - opens on the next Break"
                    : used ? $"{H("Healing Shrine", ShrineColor)} here - recharging"
                    : $"{H("Healing Shrine", ShrineColor)} here - heal up!";
            case PingKind.Rift:
                return closed ? $"{H("Cursed Rift", RiftColor)} here - opens on the next Break"
                    : used ? $"{H("Cursed Rift", RiftColor)} here - already used"
                    : $"{H("Cursed Rift", RiftColor)} here - sacrifice for a mutation!";
            case PingKind.TeamChallengeFound:
                return closed ? $"{H("Team Challenge", ChallengeColor)} here - not ready yet"
                    : $"{H("Team Challenge", ChallengeColor)} here - ready up!";
            case PingKind.TeamChallengeStart: return $"{H("Team Challenge", ChallengeColor)} ready - let's start!";
            case PingKind.TeamChallengeReward: return $"{H("Team Challenge", ChallengeColor)} won - claim the reward!";
            case PingKind.TeamChallengeDone: return $"{H("Team Challenge", ChallengeColor)} completed";
            case PingKind.TeamChallengeFailed:
                return $"{H("Team Challenge", ChallengeColor)} " + FailedTrashTalk[Random.Range(0, FailedTrashTalk.Length)];
            case PingKind.Traversal:
                return closed || used ? $"{H("Traversal Challenge", ChallengeColor)} here"
                    : $"{H("Traversal Challenge", ChallengeColor)} - help me cross!";
            case PingKind.StickTogether: return H("Stick together") + "!";
            default: return $"{H("Look")} here";
        }
    }
}
