using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using UnityEngine;

// Emoji reactions, HUD side (see docs/emoji.md). Reads the local player-one emoji inputs (right stick flick
// on a gamepad, keys 1-4 on keyboard, the scene-authored EmojiPickerWidget on touch) into a PlayEmojiCommand, and turns
// every EmojiPlayed event (any player, online included) into an EmojiBubbleWidget above that hero.
// Self-building like PingFeedWidget (no visuals of its own) - GameplayUiController.QStart calls Ensure().
public class EmojiWidget : MonoBehaviour
{
    private const float FlickThreshold = 0.75f;   // right stick past this fires
    private const float RearmThreshold = 0.35f;   // ...and must come back under this before the next one
    private const float LocalResendGuard = 0.25f; // seconds - the sim has the real cooldown, this just avoids spamming commands

    private static readonly KeyCode[] Keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

    public static EmojiWidget Instance { get; private set; }

    private bool _stickArmed = true;
    private float _lastSentTime = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    // Parents under the first Canvas found beside the caller (the gameplay HUD canvas).
    public static void Ensure(Component anchor)
    {
        if (Instance != null)
            return;

        Canvas canvas = anchor.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        var go = new GameObject("EmojiWidget", typeof(RectTransform));
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.AddComponent<EmojiWidget>();
    }

    private void Awake()
    {
        Instance = this;
        QuantumEvent.Subscribe<EventEmojiPlayed>(this, OnEmojiPlayed);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        for (int i = 0; i < Keys.Length; i++)
        {
            if (UnityEngine.Input.GetKeyDown(Keys[i]))
                Send((EmojiKind)i);
        }

        PollRightStick();
    }

    // Up = Happy, Down = Angry, Left = Cry, Right = Shock - a flick along the dominant axis.
    private void PollRightStick()
    {
        Vector2 stick = GamepadControls.Emoji;
        float magnitude = stick.magnitude;

        if (_stickArmed == false)
        {
            if (magnitude < RearmThreshold)
                _stickArmed = true;
            return;
        }

        if (magnitude < FlickThreshold)
            return;

        _stickArmed = false;
        if (Mathf.Abs(stick.y) >= Mathf.Abs(stick.x))
            Send(stick.y > 0f ? EmojiKind.Happy : EmojiKind.Angry);
        else
            Send(stick.x < 0f ? EmojiKind.Cry : EmojiKind.Shock);
    }

    public void Send(EmojiKind kind)
    {
        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        if (game == null || MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            return;

        if (Time.unscaledTime - _lastSentTime < LocalResendGuard)
            return;

        _lastSentTime = Time.unscaledTime;
        game.SendCommand(0, new PlayEmojiCommand { Kind = (byte)kind });
    }

    // The local player-one hero, for the picker's per-hero sprites.
    public static EntityRef LocalEntity =>
        MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.IsLocalPlayerSetup ? MyLocalPlayer.Instance.EntityRef : EntityRef.None;

    private void OnEmojiPlayed(EventEmojiPlayed e)
    {
        CharacterData data = EmojiPresentation.GetData(e.Owner);
        Sprite sprite = EmojiPresentation.GetSprite(e.Owner, e.Kind);
        CharView view = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetCharViewByEntityRef(e.Owner) : null;
        if (view != null)
            EmojiBubbleWidget.Spawn(e.Player, view.transform, sprite, data);
    }
}
