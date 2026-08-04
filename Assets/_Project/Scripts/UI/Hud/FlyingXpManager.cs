using Quantum;
using QuantumUser.View;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Pool;

// Spawns one flying icon per EventExpOrbCollected - purely cosmetic, no local-player filtering
// unlike DamageFeedbackManager: exp is shared co-op state (see Experience.qtn), so every client's
// bar advances together and every client plays this for every pickup, regardless of who physically
// walked over the orb.
public class FlyingXpManager : QuantumGlobalMonoBehaviour
{
    public static FlyingXpManager Instance;

    [SerializeField] private FlyingXpWidget widgetPrefab;
    [SerializeField, Tooltip("HUD canvas slot the flying icons live under - same idea as DamageFeedbackManager's widgetParent.")]
    private Transform widgetParent;
    [SerializeField, Tooltip("Instances built up front so the opening pickups of a fight don't pay an Instantiate cost.")]
    private int prewarmCount = 6;

    private Canvas _canvas;
    private Camera _worldCamera;
    private ObjectPool<FlyingXpWidget> _pool;

    private void Awake()
    {
        Instance = this;

        // The "prefab" is a scene object, so it renders as a stray icon on the HUD until it's
        // switched off. Clones inherit the off state; the pool raises each one on Get.
        widgetPrefab.gameObject.SetActive(false);
    }

    // Camera, pool and subscription all wait for QStart rather than Awake - same reasoning as
    // DamageFeedbackManager: Camera.main and the canvas have to resolve before a pooled widget can
    // be handed them, and no pickup event can reach us before the first verified frame anyway.
    public override void QStart(QuantumGame game)
    {
        _canvas = widgetParent.GetComponentInParent<Canvas>();
        _worldCamera = Camera.main;
        _pool = CreatePool();

        Prewarm(prewarmCount);

        QuantumEvent.Subscribe<EventExpOrbCollected>(this, OnExpOrbCollected);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);

        if (Instance == this)
            Instance = null;
    }

    public override void QUpdate(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    private void OnExpOrbCollected(EventExpOrbCollected e)
    {
        if (ExpBarUiWidget.Instance == null || ExpBarUiWidget.Instance.LandingPoint == null)
        {
            LogHelper.Warn("FlyingXp", "No ExpBarUiWidget in the scene - pickup effect skipped.");
            return;
        }

        FlyingXpWidget widget = _pool.Get();
        widget.Play(e.Position.ToUnityVector3(), ExpBarUiWidget.Instance.LandingPoint, _pool.Release);
    }

    private ObjectPool<FlyingXpWidget> CreatePool()
    {
        return new ObjectPool<FlyingXpWidget>(
            createFunc: CreateWidget,
            actionOnGet: widget => widget.gameObject.SetActive(true),
            actionOnRelease: widget => widget.gameObject.SetActive(false),
            actionOnDestroy: widget => Destroy(widget.gameObject));
    }

    private FlyingXpWidget CreateWidget()
    {
        var widget = Instantiate(widgetPrefab, widgetParent);
        widget.Setup(_canvas, _worldCamera);
        return widget;
    }

    private void Prewarm(int count)
    {
        var buffer = new FlyingXpWidget[count];
        for (int i = 0; i < count; i++)
            buffer[i] = _pool.Get();
        for (int i = 0; i < count; i++)
            _pool.Release(buffer[i]);
    }
}
