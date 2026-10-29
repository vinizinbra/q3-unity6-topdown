using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One off-screen teammate indicator (see OffscreenPlayerIndicatorManager / docs/offscreen-indicators.md):
// a hex frame holding the hero's live in-game head (the rig's BlobAnimationView.Head sprite, NOT the
// CharacterData.UIHead portrait; the torso sprite - the whole body - for a headless rig like Capy), a border + arrow tinted with the hero's RingColor (pulsing
// toward DownedColor at low HP, solid DownedColor while Downed - with a matching DownedColor glow behind the hex),
// and an optional distance label. The manager owns all placement; this only paints and pops.
// When that teammate pings while off-screen, hex rings ripple out from behind the frame in the border's
// colour (PlayPingRipple) - the edge-of-screen counterpart of PingWorldRippleWidget.
//
// The head stays upright - only `arrowPivot` turns. The distance label sits on the
// screen-centre side, so it never clips off the edge whichever edge the indicator is pinned to.
public class OffscreenPlayerIndicatorWidget : MonoBehaviour
{
    private const string LogTag = "OffscreenIndicator";

    [SerializeField] private RectTransform selfRect;
    [SerializeField, Tooltip("Scaled for the pop-in/out - normally the visual root under selfRect.")]
    private RectTransform visualRoot;
    [SerializeField, Tooltip("Shows the rig head sprite, sized to sit fully inside the hex - no Mask, since a stencil Mask cuts a jagged, un-antialiased edge.")]
    private Image headImage;
    [SerializeField, Tooltip("Hex outline - tinted RingColor / DownedColor.")]
    private Image borderImage;
    [SerializeField, Tooltip("Rotated so its local +Y points at the hero. The arrow child is authored above the hex, pointing up.")]
    private RectTransform arrowPivot;
    [SerializeField, Tooltip("Tinted together with the border.")]
    private Image arrowImage;
    [SerializeField, Tooltip("Optional distance readout.")]
    private TMP_Text distanceLabel;
    [SerializeField, Tooltip("How far from the centre the distance label sits, on the screen-centre side (opposite the hero).")]
    private float distanceLabelOffset = 62f;

    [Header("Ping ripple")]
    [SerializeField, Tooltip("Hex outline Images behind the frame, animated as expanding/fading rings half a pulse apart (same feel as PingWorldRippleWidget). Hidden while idle.")]
    private Image[] pingRings;
    [SerializeField, Tooltip("How long a ping keeps rippling (seconds, unscaled - keeps going through sim pauses).")]
    private float pingRippleDuration = 3f;
    [SerializeField, Tooltip("Seconds for one ring to expand and fade.")]
    private float pingPulsePeriod = 1f;
    [SerializeField, Tooltip("Ring scale at the end of a pulse (1 = the hex frame's own size).")]
    private float pingMaxScale = 2.2f;

    [Header("Pop")]
    [SerializeField] private float appearDuration = 0.25f;
    [SerializeField] private float disappearDuration = 0.15f;

    private OffscreenPlayerIndicatorManager _manager;
    private EntityRef _entityRef;
    private Transform _followTarget;
    private SpriteRenderer _headRenderer;
    private Color _heroColor = Color.white;
    private bool _shown;
    private int _lastDistance = -1;
    private Tween _popTween;
    private float _pingStartTime = float.NegativeInfinity;
    private float _lowHealthPhase;
    private Image _glowImage;
    private bool _glowMissing;

    public void Setup(OffscreenPlayerIndicatorManager manager, EntityRef entityRef, Transform followTarget)
    {
        _manager = manager;
        _entityRef = entityRef;
        _followTarget = followTarget;

        if (distanceLabel != null)
            distanceLabel.gameObject.SetActive(manager.ShowDistance);

        // Starts active-but-collapsed so the first SetShown(true) pops it in.
        visualRoot.localScale = Vector3.zero;
        gameObject.SetActive(true);
        _shown = false;
        SetRingsVisible(false);
    }

    public bool IsShown => _shown;

    public void PlayPingRipple()
    {
        _pingStartTime = Time.unscaledTime;
        SetRingsVisible(true);
    }

    private void Update()
    {
        if (pingRings == null || pingRings.Length == 0 || float.IsNegativeInfinity(_pingStartTime))
            return;

        float age = Time.unscaledTime - _pingStartTime;
        if (age >= pingRippleDuration)
        {
            _pingStartTime = float.NegativeInfinity;
            SetRingsVisible(false);
            return;
        }

        // Rings are evenly staggered across one period so there's always one expanding; each fades out as
        // it grows, and the whole ripple fades over its last pulse instead of cutting mid-ring.
        float tailFade = Mathf.Clamp01((pingRippleDuration - age) / pingPulsePeriod);
        for (int i = 0; i < pingRings.Length; i++)
        {
            float t = (age / pingPulsePeriod + (float)i / pingRings.Length) % 1f;
            float eased = 1f - (1f - t) * (1f - t);
            pingRings[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, pingMaxScale, eased);

            // Read live from the border so the rings follow it (RingColor, or DownedColor mid-ripple).
            Color c = borderImage.color;
            c.a = (1f - t) * tailFade;
            pingRings[i].color = c;
        }
    }

    private void SetRingsVisible(bool visible)
    {
        if (pingRings == null)
            return;

        foreach (var ring in pingRings)
        {
            if (ring != null)
                ring.enabled = visible;
        }
    }

    private void OnDestroy()
    {
        _popTween.Stop();
    }

    // False when there is nothing to point at (view gone, hidden, or KO with hideWhenKO).
    public bool TryResolveTarget(Frame frame, out Vector3 worldPosition)
    {
        worldPosition = default;

        if (_followTarget == null || _followTarget.gameObject.activeInHierarchy == false || frame.Exists(_entityRef) == false)
            return false;

        if (_manager.HideWhenKO && frame.TryGet<PlayerLifeState>(_entityRef, out var life) && life.State == PlayerLifeStateKind.KO)
            return false;

        worldPosition = _followTarget.position;
        return true;
    }

    public void Refresh(Frame frame, Vector2 anchoredPosition, Vector2 direction, float distance)
    {
        selfRect.anchoredPosition = anchoredPosition;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        arrowPivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        RefreshHead(frame);

        float urgency = ResolveUrgency(frame);
        Color tint = Color.Lerp(_heroColor, _manager.DownedColor, urgency);
        borderImage.color = tint;
        arrowImage.color = tint;
        RefreshGlow(urgency);

        if (distanceLabel != null && _manager.ShowDistance)
        {
            distanceLabel.rectTransform.anchoredPosition = -direction * distanceLabelOffset;

            int rounded = Mathf.RoundToInt(distance);
            if (rounded != _lastDistance)
            {
                _lastDistance = rounded;
                distanceLabel.SetText("{0}m", rounded);
            }
        }
    }

    // Three steps toward "go help", as 0..1 (0 = RingColor / no glow, 1 = DownedColor / full glow): 0 while
    // healthy -> pulsing 0..1 below lowHealthPercent (faster as HP drops) -> solid 1 once Downed. The phase
    // accumulates instead of using Time directly so the pulse doesn't jump when its period changes with HP.
    private float ResolveUrgency(Frame frame)
    {
        if (frame.TryGet<PlayerLifeState>(_entityRef, out var life) && life.State == PlayerLifeStateKind.Downed)
            return 1f;

        if (_manager.LowHealthPercent <= 0f || frame.TryGet<Health>(_entityRef, out var health) == false || health.MaxHealth <= FP._0 || health.CurrentHealth <= FP._0)
        {
            _lowHealthPhase = 0f;
            return 0f;
        }

        float ratio = (health.CurrentHealth / health.MaxHealth).AsFloat;
        if (ratio > _manager.LowHealthPercent)
        {
            _lowHealthPhase = 0f;
            return 0f;
        }

        float period = Mathf.Lerp(_manager.LowHealthPulseFast, _manager.LowHealthPulseSlow, ratio / _manager.LowHealthPercent);
        _lowHealthPhase = (_lowHealthPhase + Time.unscaledDeltaTime / Mathf.Max(period, 0.05f)) % 1f;

        // Starts at 0 (RingColor), peaks at 1 (DownedColor) mid-pulse.
        return 0.5f - 0.5f * Mathf.Cos(_lowHealthPhase * 2f * Mathf.PI);
    }

    // Soft DownedColor halo hugging the hex, faded by urgency. The sprite is the border's pre-generated glow from
    // FocusOutlineSet (Tools/RiftRaiders/UI/Generate Focus Outlines - RR_HexagonOutline is one of its extra
    // sources), created lazily as the first child of visualRoot so it sits behind the frame and pops with it.
    private void RefreshGlow(float urgency)
    {
        float alpha = urgency * _manager.LowHealthGlowAlpha;
        if (alpha <= 0f)
        {
            if (_glowImage != null)
                _glowImage.enabled = false;
            return;
        }

        if (EnsureGlow() == false)
            return;

        Color c = _manager.DownedColor;
        c.a = alpha;
        _glowImage.color = c;
        _glowImage.enabled = true;
    }

    private bool EnsureGlow()
    {
        if (_glowImage != null)
            return true;
        if (_glowMissing)
            return false;

        FocusOutlineSet set = FocusOutlineSet.Instance;
        if (set == null || set.TryGet(borderImage.sprite, out FocusOutlineSet.Entry entry) == false || entry.focus == null)
        {
            _glowMissing = true;
            LogHelper.Warn(LogTag, $"No glow sprite for '{(borderImage.sprite != null ? borderImage.sprite.name : "(no sprite)")}' - run Tools/RiftRaiders/UI/Generate Focus Outlines.");
            return false;
        }

        var go = new GameObject("LowHealthGlow", typeof(RectTransform), typeof(CanvasRenderer));
        var rect = (RectTransform)go.transform;
        rect.SetParent(visualRoot, false);
        rect.SetAsFirstSibling();

        // Same centre as the border, grown by the sprite's padding converted to canvas units.
        RectTransform border = borderImage.rectTransform;
        float pad = entry.padding * border.rect.width / entry.source.rect.width;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = border.anchoredPosition;
        rect.sizeDelta = border.rect.size + new Vector2(pad, pad) * 2f;

        _glowImage = go.AddComponent<Image>();
        _glowImage.sprite = entry.focus;
        _glowImage.raycastTarget = false;
        _glowImage.enabled = false;
        return true;
    }

    public void SetShown(bool shown)
    {
        if (shown == _shown)
            return;

        _shown = shown;
        _popTween.Stop();

        _popTween = shown
            ? Tween.Scale(visualRoot, Vector3.one, appearDuration, Ease.OutBack)
            : Tween.Scale(visualRoot, Vector3.zero, disappearDuration, Ease.InBack);
    }

    // The live rig head - re-read every frame so it follows whatever the head is showing (cheap: a
    // sprite reference compare). Hero colour is resolved on the same pass the renderer is found.
    // The rig's head sprite, or - for a rig with no separate head, like Capy - the torso's, which is then the
    // whole body (preserveAspect fits it inside the hex).
    private static SpriteRenderer ResolveHeadRenderer(BlobAnimationView blobView)
    {
        if (blobView == null)
            return null;

        SpriteRenderer renderer = blobView.Head != null ? blobView.Head.GetComponentInChildren<SpriteRenderer>(true) : null;
        if (renderer == null && blobView.Torso != null)
            renderer = blobView.Torso.GetComponentInChildren<SpriteRenderer>(true);

        return renderer;
    }

    private void RefreshHead(Frame frame)
    {
        if (_headRenderer == null)
        {
            CharView charView = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetCharViewByEntityRef(_entityRef) : null;
            BlobAnimationView blobView = charView != null ? charView.GetComponent<BlobAnimationView>() : null;
            _headRenderer = ResolveHeadRenderer(blobView);

            if (frame.TryGet<CharacterStats>(_entityRef, out var stats) && stats.CharacterData.IsValid)
            {
                CharacterData data = frame.FindAsset(stats.CharacterData);
                if (data != null)
                    _heroColor = data.RingColor;
            }
        }

        Sprite sprite = _headRenderer != null ? _headRenderer.sprite : null;
        if (sprite != null && headImage.sprite != sprite)
        {
            headImage.sprite = sprite;
            headImage.enabled = true;
        }
    }
}
