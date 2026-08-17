using Quantum;
using QuantumUser.View;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Pool;

// Spawns one flying pickup sprite per currency-collected event (Exp/Coin/RiftShard) - purely
// cosmetic, no local-player filtering: every client plays this for every pickup regardless of who
// physically walked over the orb, same as the currency totals themselves being shared/global state
// (see Experience.qtn/Coins.qtn/RiftShards.qtn). One manager/widget pair for all three currencies
// (CurrencyType-driven) rather than one per currency, replacing the old UI-space FlyingXpManager/
// FlyingXpWidget (Exp-only, flew to the HUD bar instead of the collecting character).
//
// Flies a world-space sprite (FlyingCurrencyWidget) from the orb's collected position to the
// COLLECTOR's own live position (EntityViewManager.Instance.GetEntityTransform, re-resolved every
// frame since the character moves mid-flight) - visible on every client regardless of whether the
// collector is the local player or a teammate, since every character exists in every client's
// world. On arrival, flashes that character in a currency-specific color via HitFeedback.
// FlashPickup - deliberately LOWER priority than a hit-taken flash (see that method's own comment),
// so a pickup glow can never visually stomp a more important reaction happening at the same moment.
// The flash colors/duration themselves live on HitFeedback, not here - same place every other flash
// color (hit/heal/shield/rift mark) is already authored, so a currency's pickup color isn't split
// across two components.
public class FlyingCurrencyManager : QuantumGlobalMonoBehaviour
{
    public static FlyingCurrencyManager Instance;

    [SerializeField] private FlyingCurrencyWidget widgetPrefab;
    [SerializeField, Tooltip("Parent transform the pooled flying sprites live under - a plain world-space transform, not a Canvas.")]
    private Transform widgetParent;
    [SerializeField, Tooltip("Instances built up front so the opening pickups of a fight don't pay an Instantiate cost.")]
    private int prewarmCount = 8;

    private ObjectPool<FlyingCurrencyWidget> _pool;

    private void Awake()
    {
        Instance = this;

        // The "prefab" is a scene object, so it renders as a stray sprite until switched off.
        // Clones inherit the off state; the pool raises each one on Get.
        if (widgetPrefab != null)
            widgetPrefab.gameObject.SetActive(false);
    }

    // Pool and subscription wait for QStart rather than Awake - no pickup event can reach us
    // before the first verified frame anyway, same reasoning the old FlyingXpManager used.
    public override void QStart(QuantumGame game)
    {
        _pool = CreatePool();
        Prewarm(prewarmCount);

        QuantumEvent.Subscribe<EventExpOrbCollected>(this, e => OnCollected(CurrencyType.Experience, e.Collector, e.Position.ToUnityVector3()));
        QuantumEvent.Subscribe<EventCoinCollected>(this, e => OnCollected(CurrencyType.Coin, e.Collector, e.Position.ToUnityVector3()));
        QuantumEvent.Subscribe<EventRiftShardCollected>(this, e => OnCollected(CurrencyType.RiftShard, e.Collector, e.Position.ToUnityVector3()));
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

    private void OnCollected(CurrencyType type, EntityRef collector, Vector3 worldPosition)
    {
        FlyingCurrencyWidget widget = _pool.Get();

        widget.Play(SpriteManager.GetSprite(type.ToString()), worldPosition,
            () => EntityViewManager.Instance.GetEntityTransform(collector),
            finishedWidget => OnArrived(type, collector, finishedWidget));
    }

    private void OnArrived(CurrencyType type, EntityRef collector, FlyingCurrencyWidget widget)
    {
        _pool.Release(widget);

        Transform target = EntityViewManager.Instance.GetEntityTransform(collector);
        HitFeedback hitFeedback = target != null ? target.GetComponentInChildren<HitFeedback>() : null;
        hitFeedback?.FlashPickup(type);

        // Exp keeps its existing "bar catches up + flashes" reaction, on top of the character
        // flash above - Coin/RiftShard have no equivalent bar to catch up (CurrencyUiWidget's own
        // punch-on-change already covers those independently of this arrival).
        if (type == CurrencyType.Experience)
            ExpBarUiWidget.Instance?.Flash();
    }

    private ObjectPool<FlyingCurrencyWidget> CreatePool()
    {
        return new ObjectPool<FlyingCurrencyWidget>(
            createFunc: () => Instantiate(widgetPrefab, widgetParent),
            actionOnGet: widget => widget.gameObject.SetActive(true),
            actionOnRelease: widget => widget.gameObject.SetActive(false),
            actionOnDestroy: widget => Destroy(widget.gameObject));
    }

    private void Prewarm(int count)
    {
        var buffer = new FlyingCurrencyWidget[count];

        for (int i = 0; i < count; i++)
            buffer[i] = _pool.Get();

        for (int i = 0; i < count; i++)
            _pool.Release(buffer[i]);
    }
}
