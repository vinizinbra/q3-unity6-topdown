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

    [Header("Sound")]
    [SerializeField, Tooltip("Played when an XP orb actually REACHES the collector, not when it's picked up. Every kill drops one and they're hoovered up in bulk, so this is by far the most repeated pickup in the game - author a cooldown (~0.08s) on the SoundData and keep it quiet, or tick Local Player Only, or leave it empty. It is the easiest sound in the project to make annoying.")]
    private SoundData experienceCollectSound;

    [SerializeField, Tooltip("Played when a Coin reaches the collector. Gated by per-tier CoinDropChance, so it's rare enough to be a real reward sound - worth making distinct and satisfying.")]
    private SoundData coinCollectSound;

    [SerializeField, Tooltip("Played when a Rift Shard reaches the collector. Same reasoning as Coin - rare by drop chance, so it can afford to be prominent.")]
    private SoundData riftShardCollectSound;

    [SerializeField, Tooltip("Raises the XP pickup's pitch a step for each orb collected in quick succession, resetting after a gap. Turns a burst of pickups into an ascending run instead of the same tick repeated - the difference between feedback that rewards a big clear and feedback that just reports one. Applies to Experience ONLY: Coin and Rift Shard are rare by drop chance, so a streak would never really trigger, and a rare reward should sound identical every time to stay recognisable.")]
    private bool experiencePitchStreak = true;

    [SerializeField, Tooltip("Gap after which the streak resets to its base pitch. Roughly how long a burst of orbs takes to finish arriving - too short and a steady stream never climbs, too long and unrelated pickups keep stacking upward.")]
    private float experienceStreakResetTime = 0.4f;

    [SerializeField, Tooltip("Semitones added per step. 1 is a chromatic climb; 2 (whole tones) reads more musical and gets high faster.")]
    private float experienceStreakSemitones = 1f;

    [SerializeField, Tooltip("Cap on how far the streak can climb, so a long fight doesn't end up somewhere shrill and comical.")]
    private int experienceStreakMaxSteps = 8;

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

        PlayCollectSound(type, collector, target);

        // Exp keeps its existing "bar catches up + flashes" reaction, on top of the character
        // flash above - Coin/RiftShard have no equivalent bar to catch up (CurrencyUiWidget's own
        // punch-on-change already covers those independently of this arrival).
        if (type == CurrencyType.Experience)
            ExpBarUiWidget.Instance?.Flash();
    }

    // On ARRIVAL rather than on collection, so it lands with the flash and the bar catching up
    // instead of firing while the orb is still flying across the screen.
    //
    // Routed through EntitySound so each asset can choose how it behaves for a teammate's pickup -
    // Local Player Only suits Experience (four players hoovering orbs is pure noise), while Coin and
    // Rift Shard are rare enough to be worth hearing whoever grabbed them.
    private void PlayCollectSound(CurrencyType type, EntityRef collector, Transform target)
    {
        SoundData sound = type switch
        {
            CurrencyType.Experience => experienceCollectSound,
            CurrencyType.Coin => coinCollectSound,
            CurrencyType.RiftShard => riftShardCollectSound,
            _ => null,
        };

        if (sound == null || target == null)
            return;

        SoundHandle handle = EntitySound.PlayAt(sound, target.position, collector);

        if (type != CurrencyType.Experience || experiencePitchStreak == false)
            return;

        // Only advance on a play that actually happened. The asset's own cooldown suppresses most
        // orbs in a dense burst (returning an invalid handle), and counting those would race the
        // pitch to its cap in a fraction of a second while barely anything was audible.
        if (handle.IsValid == false)
            return;

        float now = Time.unscaledTime;

        _experienceStreak = now - _lastExperienceCollectTime > experienceStreakResetTime
            ? 0
            : Mathf.Min(_experienceStreak + 1, Mathf.Max(0, experienceStreakMaxSteps));

        _lastExperienceCollectTime = now;

        if (_experienceStreak > 0)
        {
            // Equal temperament: each semitone is a factor of 2^(1/12), so the climb is musical
            // rather than a linear pitch ramp (which goes shrill unevenly).
            float multiplier = Mathf.Pow(2f, _experienceStreak * experienceStreakSemitones / 12f);
            handle.ScalePitch(multiplier);
        }
    }

    private int _experienceStreak;
    private float _lastExperienceCollectTime;

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
