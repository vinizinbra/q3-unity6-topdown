using System.Collections.Generic;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using UnityEngine;

// Off-screen teammate indicators (see docs/offscreen-indicators.md) - Mario-style hex heads pinned to the
// screen edge pointing at every REMOTE player (online teammates and bots) whose hero has left the camera
// view. Local feedback only: no sim state, each client just projects the other heroes' views.
//
// Same manager+widget pattern as CharacterUiWidgetManager: CharView registers every non-local hero from
// its own Initialize/DeInitialize, this spawns one OffscreenPlayerIndicatorWidget per hero by cloning the
// inactive `template` child of the authored prefab (Prefabs/UI/OffscreenPlayerIndicators.prefab), and
// lays them all out once per LateUpdate. Local couch-coop players never register - the camera already
// frames every local slot (see MyLocalPlayer.Register), so they can't be off-screen.
//
// Geometry is done in this rect's own local space (it stretches over the whole canvas, pivot centred,
// and every widget is anchored to its centre, so a local point IS a widget's anchoredPosition). The
// usable area is the canvas rect intersected with Screen.safeArea, so pinned heads never sit under a
// notch / rounded corner.
public class OffscreenPlayerIndicatorManager : MonoBehaviour
{
    public static OffscreenPlayerIndicatorManager Instance { get; private set; }

    [SerializeField, Tooltip("Inactive child cloned once per remote hero.")]
    private OffscreenPlayerIndicatorWidget template;

    [Header("Layout")]
    [SerializeField, Tooltip("How far inside the (safe-area) screen edge the indicator's centre is pinned, in canvas units - roughly half the hex plus the arrow, so nothing clips.")]
    private float edgePadding = 80f;
    [SerializeField, Tooltip("A hero counts as on-screen while its projected point is at least this far inside the screen edge (canvas units). Positive = the indicator appears a little before the hero fully leaves; negative = only once it is well past the edge.")]
    private float visibleMargin = 0f;
    [SerializeField, Tooltip("World offset added to the hero's view position before projecting - aims at the body rather than the feet.")]
    private Vector3 worldOffset = new Vector3(0f, 1f, 0f);

    [Header("Look")]
    [SerializeField, Tooltip("Border/arrow tint while the teammate is Downed (replaces the hero's RingColor).")]
    private Color downedColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("At or below this HP fraction (Health only, not Shield - same rule as HurtOverlayUiWidget) the border/arrow pulse between RingColor and DownedColor. 0 = off.")]
    private float lowHealthPercent = 0.3f;
    [SerializeField, Tooltip("Seconds per pulse right at the threshold.")]
    private float lowHealthPulseSlow = 0.8f;
    [SerializeField, Tooltip("Seconds per pulse as HP approaches 0 - the pulse speeds up like a heartbeat as the teammate gets closer to Downed.")]
    private float lowHealthPulseFast = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Peak alpha of the DownedColor glow behind the hex - follows the low-HP pulse, steady while Downed. 0 = no glow.")]
    private float lowHealthGlowAlpha = 0.85f;
    [SerializeField, Tooltip("Hide the indicator once that teammate is KO (dead end - nothing to go do there).")]
    private bool hideWhenKO = true;
    [SerializeField, Tooltip("Show the distance (world units, XZ) from local player one under the head.")]
    private bool showDistance = true;

    private readonly Dictionary<EntityRef, OffscreenPlayerIndicatorWidget> _widgets = new Dictionary<EntityRef, OffscreenPlayerIndicatorWidget>();

    private RectTransform _rect;
    private Canvas _canvas;

    public Color DownedColor => downedColor;
    public float LowHealthPercent => lowHealthPercent;
    public float LowHealthPulseSlow => lowHealthPulseSlow;
    public float LowHealthPulseFast => lowHealthPulseFast;
    public float LowHealthGlowAlpha => lowHealthGlowAlpha;
    public bool HideWhenKO => hideWhenKO;
    public bool ShowDistance => showDistance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Awake()
    {
        Instance = this;
        _rect = (RectTransform)transform;
        _canvas = GetComponentInParent<Canvas>();

        if (template != null)
            template.gameObject.SetActive(false);

        QuantumEvent.Subscribe<EventPingPlaced>(this, OnPingPlaced);
    }

    // A teammate pinging while their indicator is on the edge ripples it (in the border's colour). On-screen
    // pingers are skipped - the world ripple already shows where they are.
    private void OnPingPlaced(EventPingPlaced e)
    {
        if (_widgets.TryGetValue(e.Owner, out var widget) && widget != null && widget.IsShown)
            widget.PlayPingRipple();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Register(EntityRef entityRef, Transform followTarget)
    {
        if (template == null || _widgets.ContainsKey(entityRef))
            return;

        var widget = Instantiate(template, template.transform.parent);
        widget.name = $"Indicator {entityRef}";
        widget.Setup(this, entityRef, followTarget);
        _widgets.Add(entityRef, widget);
    }

    public void Unregister(EntityRef entityRef)
    {
        if (_widgets.TryGetValue(entityRef, out var widget) == false)
            return;

        _widgets.Remove(entityRef);
        if (widget != null)
            Destroy(widget.gameObject);
    }

    private void LateUpdate()
    {
        if (_widgets.Count == 0)
            return;

        QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
        Frame frame = game?.Frames.Predicted;
        Camera worldCamera = FollowCamera.WorldCamera;
        Transform localPlayer = ResolveLocalPlayerTransform();

        // Nothing meaningful to point from (no camera yet, or no local hero to measure distance from).
        if (frame == null || worldCamera == null || localPlayer == null)
        {
            foreach (var widget in _widgets.Values)
                widget.SetShown(false);
            return;
        }

        Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        Rect safeRect = GetSafeRect(eventCamera);
        Rect pinRect = Inset(safeRect, edgePadding);
        Rect visibleRect = Inset(safeRect, visibleMargin);

        foreach (var widget in _widgets.Values)
        {
            if (widget.TryResolveTarget(frame, out Vector3 worldPosition) == false)
            {
                widget.SetShown(false);
                continue;
            }

            worldPosition += worldOffset;
            Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPosition);
            bool behindCamera = screenPoint.z < 0f;

            // A point behind the camera projects mirrored through the screen centre - flip it back so
            // the arrow still points the right way (rare with the fixed top-down camera, but free).
            if (behindCamera)
                screenPoint = new Vector3(Screen.width - screenPoint.x, Screen.height - screenPoint.y, 0f);

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, eventCamera, out Vector2 localPoint) == false)
            {
                widget.SetShown(false);
                continue;
            }

            if (behindCamera == false && visibleRect.Contains(localPoint))
            {
                widget.SetShown(false);
                continue;
            }

            Vector2 center = pinRect.center;
            Vector2 direction = localPoint - center;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.up;

            Vector2 pinned = ClampToRect(center, direction, pinRect);

            Vector3 delta = worldPosition - worldOffset - localPlayer.position;
            float distance = new Vector2(delta.x, delta.z).magnitude;

            widget.Refresh(frame, pinned, direction.normalized, distance);
            widget.SetShown(true);
        }
    }

    // Walks from the centre along `direction` until it hits the rect's border.
    private static Vector2 ClampToRect(Vector2 center, Vector2 direction, Rect rect)
    {
        float halfWidth = rect.width * 0.5f;
        float halfHeight = rect.height * 0.5f;

        float scaleX = Mathf.Abs(direction.x) > 0.0001f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
        float scaleY = Mathf.Abs(direction.y) > 0.0001f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;

        return center + direction * Mathf.Min(scaleX, scaleY);
    }

    private static Rect Inset(Rect rect, float amount)
    {
        float x = Mathf.Min(amount, rect.width * 0.5f);
        float y = Mathf.Min(amount, rect.height * 0.5f);
        return new Rect(rect.xMin + x, rect.yMin + y, rect.width - x * 2f, rect.height - y * 2f);
    }

    // This rect intersected with Screen.safeArea, in this rect's local space.
    private Rect GetSafeRect(Camera eventCamera)
    {
        Rect rect = _rect.rect;
        Rect safeArea = Screen.safeArea;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, safeArea.min, eventCamera, out Vector2 min) == false
            || RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, safeArea.max, eventCamera, out Vector2 max) == false)
            return rect;

        float xMin = Mathf.Max(rect.xMin, min.x);
        float yMin = Mathf.Max(rect.yMin, min.y);
        float xMax = Mathf.Min(rect.xMax, max.x);
        float yMax = Mathf.Min(rect.yMax, max.y);

        return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : rect;
    }

    private static Transform ResolveLocalPlayerTransform()
    {
        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false || EntityViewManager.Instance == null)
            return null;

        return EntityViewManager.Instance.GetEntityTransform(MyLocalPlayer.Instance.EntityRef);
    }
}
