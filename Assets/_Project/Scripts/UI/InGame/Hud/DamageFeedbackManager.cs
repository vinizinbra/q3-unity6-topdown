using System.Collections.Generic;
using NaughtyAttributes;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Pool;

// Spawns one floating number per EventEntityDamaged/EventEntityHealed the local player was part of -
// hits (or heals) they took, and hits (or heals) they dealt. Everyone else's is dropped on purpose:
// numbers over an enemy some other player is shooting tell this player nothing, and in a busy fight
// they bury the ones that do.
public class DamageFeedbackManager : QuantumGlobalMonoBehaviour
{
    public static DamageFeedbackManager Instance;

    [SerializeField, SoundDataPicker, Tooltip("Played when the LOCAL player lands a critical hit - never for a teammate's crit, and never for one taken. Deliberately gated on the same resolution the crit NUMBER uses, so the sound and the number always agree about whose hit it was.\n\nA multi-pellet weapon can crit several times in one tick, so author a small cooldown on the SoundData (~0.05s) or a shotgun crit fires one sound per pellet. Untick its Spatial flag to have it read as flat UI feedback rather than something happening out in the world.")]
    private SoundData criticalHitSound;

    [SerializeField, SoundDataPicker, Tooltip("Played when the LOCAL player lands an ordinary (non-critical) hit - never for a teammate's, never for one taken, and never for a damage-over-time tick.\n\nThis is the single highest-frequency sound in the game: every bullet, every pellet, every enemy in an explosion. Author a cooldown on the SoundData (~0.05s) and keep the Impacts group budget tight, or a shotgun into a crowd fires dozens of copies in one tick.")]
    private SoundData hitSound;

    [SerializeField] private DamageNumberUiWidget widgetPrefab;
    [SerializeField, Tooltip("HUD canvas slot the numbers live under - same idea as CharacterUiWidgetManager's widgetParent.")]
    private Transform widgetParent;
    [SerializeField, Tooltip("Instances built up front so the opening hits of a fight don't pay an Instantiate cost.")]
    private int prewarmCount = 12;

    [SerializeField, Tooltip("Offset from the hit entity's origin, so the number starts around its body instead of at its feet.")]
    private Vector3 worldOffset = new Vector3(0f, 1.5f, 0f);

    [SerializeField, Tooltip("Extra Play() start delay added per number spawned on the same Unity frame (e.g. every shotgun pellet hitting one target in the same tick), so they pop in one after another instead of all animating from t=0.")]
    private float burstStaggerStep = 0.03f;
    [SerializeField, Tooltip("Caps how much delay a single dense burst can accumulate, so a big pellet count doesn't leave the last few numbers reading noticeably late.")]
    private float maxBurstStaggerDelay = 0.15f;

    private int _burstFrame = -1;
    private int _burstIndex;

    [SerializeField]
    private List<DamageNumberStyle> styles = new List<DamageNumberStyle>
    {
        new DamageNumberStyle { Kind = DamageNumberKind.TakenByMe, Color = Color.red },
        new DamageNumberStyle { Kind = DamageNumberKind.DealtByMe, Color = Color.white },
        new DamageNumberStyle
        {
            Kind = DamageNumberKind.CriticalDealtByMe, Color = new Color(1f, 0.45f, 0f),
            FontSizeMultiplier = 1.8f, PunchScaleMultiplier = 1.3f, Suffix = "!"
        },
        new DamageNumberStyle { Kind = DamageNumberKind.BurnTakenByMe, Color = new Color(1f, 0.35f, 0.1f) },
        new DamageNumberStyle { Kind = DamageNumberKind.BurnDealtByMe, Color = new Color(1f, 0.6f, 0.2f) },
        new DamageNumberStyle { Kind = DamageNumberKind.HealedTakenByMe, Color = new Color(0.4f, 1f, 0.4f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.HealedDealtByMe, Color = new Color(0.6f, 1f, 0.6f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.FrontalReducedDealtByMe, Color = Color.gray },
        new DamageNumberStyle { Kind = DamageNumberKind.HealedEnemy, Color = new Color(0.5f, 1f, 0.8f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.ShieldedTakenByMe, Color = new Color(0.4f, 0.75f, 1f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.ShieldedDealtByMe, Color = new Color(0.6f, 0.85f, 1f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.ShieldedEnemy, Color = new Color(0.6f, 0.8f, 1f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.HealedAlly, Color = new Color(0.55f, 0.95f, 0.55f), Prefix = "+" },
        new DamageNumberStyle { Kind = DamageNumberKind.ShieldedAlly, Color = new Color(0.5f, 0.8f, 1f), Prefix = "+" },

        // Reaction procs - distinct from Burn's plain orange/CriticalDealtByMe's own orange so a proc
        // reads as its own thing at a glance, not "a bigger burn tick"/"a crit". Bumped size like a
        // crit (though smaller) since these are genuine finisher/combo hits, not an ordinary tick.
        new DamageNumberStyle
        {
            Kind = DamageNumberKind.ThermalShockDealtByMe, Color = new Color(1f, 0.55f, 0.9f),
            FontSizeMultiplier = 1.4f, PunchScaleMultiplier = 1.2f
        },
        new DamageNumberStyle { Kind = DamageNumberKind.ThermalShockTakenByMe, Color = new Color(0.85f, 0.4f, 0.75f) },
        new DamageNumberStyle
        {
            Kind = DamageNumberKind.OverloadDealtByMe, Color = new Color(1f, 0.95f, 0.4f),
            FontSizeMultiplier = 1.3f, PunchScaleMultiplier = 1.2f
        },
        new DamageNumberStyle { Kind = DamageNumberKind.OverloadTakenByMe, Color = new Color(0.85f, 0.8f, 0.3f) },
    };

    private Canvas _canvas;
    private Camera _worldCamera;
    private ObjectPool<DamageNumberUiWidget> _pool;

    private void Awake()
    {
        Instance = this;

        // The "prefab" is a scene object, so it renders as a stray number on the HUD until it's
        // switched off. Clones inherit the off state; the pool raises each one on Get.
        widgetPrefab.gameObject.SetActive(false);
    }

    // Camera, pool and subscription all wait for QStart rather than Awake - Camera.main and the
    // canvas have to resolve before a pooled widget can be handed them, and no damage event can
    // reach us before the first verified frame anyway.
    public override void QStart(QuantumGame game)
    {
        _canvas = widgetParent.GetComponentInParent<Canvas>();
        _worldCamera = Camera.main;
        _pool = CreatePool();

        Prewarm(prewarmCount);

        QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
        QuantumEvent.Subscribe<EventEntityHealed>(this, OnEntityHealed);
        QuantumEvent.Subscribe<EventEntityShielded>(this, OnEntityShielded);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public override void QUpdate(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    private void OnEntityDamaged(EventEntityDamaged e)
    {
        // Silent = a passive/self-inflicted tick (e.g. SentryDecaySystem) that shouldn't read as
        // "damage" at all - no flash (HitFeedback), and no floating number either.
        if (e.Silent == true)
            return;

        if (TryResolveKind(e, out var kind) == false)
            return;

        // Deliberately NOT gated on the resolved kind: ResolveOwningPlayer collapses a sentry's hit
        // into its deployer, which is right for the NUMBER (it is your damage) but wrong for the
        // sound. A sentry fires continuously and unattended, so letting its impacts through the same
        // one-shot a trigger pull uses machine-guns the highest-frequency sound in the game for as
        // long as the sentry is alive - and it is feedback for a shot the player never took. The
        // number stays, the audio drops.
        if (IsSentrySource(e.Game.Frames.Predicted, e.Owner) == false)
            PlayImpactSound(kind, e.Position.ToUnityVector3());

        Spawn(kind, e.Damage.AsFloat, e.Position.ToUnityVector3() + worldOffset);
    }

    // Piggy-backs on the kind already resolved for the floating number rather than re-deriving
    // ownership: that resolution also traces a hit back through a Sentry chassis/barrel to the
    // player who deployed it (see ResolveOwningPlayer), so your sentry's hits count as yours -
    // which a plain `e.Owner == localPlayer` check would miss. It also means the sound and the
    // number can never disagree about whose hit it was.
    private void PlayImpactSound(DamageNumberKind kind, Vector3 position)
    {
        SoundData sound = kind switch
        {
            // A crit REPLACES the common impact rather than stacking on top of it, so the two can't
            // double up into a muddy hit. To layer them, put hitSound in criticalHitSound's own
            // Layers list - that's what layers are for, and it keeps the decision in the asset.
            DamageNumberKind.CriticalDealtByMe => criticalHitSound,

            // A frontal-reduced hit still connected, so it gets the common impact - it just doesn't
            // get the crit's reward sound (see the priority order in TryResolveKind).
            DamageNumberKind.DealtByMe or DamageNumberKind.FrontalReducedDealtByMe => hitSound,

            // Everything else deliberately silent here: BurnDealtByMe is a damage-over-time TICK,
            // not an impact, and would machine-gun this sound for the whole burn duration; the
            // TakenByMe/Healed/Shielded kinds aren't hits I landed at all.
            _ => null,
        };

        if (sound != null)
            AudioManager.PlayAt(sound, position);
    }

    // Taking a hit is checked before dealing one so self-damage (own explosion, decoy backfire)
    // reads as damage on me rather than damage I dealt.
    private bool TryResolveKind(EventEntityDamaged e, out DamageNumberKind kind)
    {
        kind = default;

        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.AnyLocalPlayerSetup == false)
            return false;

        Frame frame = e.Game.Frames.Predicted;

        if (IsLocalEntity(ResolveOwningPlayer(frame, e.Target)))
        {
            kind = ResolveElementalKind(e.Element, e.ReactionProc, taken: true);
            return true;
        }

        if (IsLocalEntity(ResolveOwningPlayer(frame, e.Owner)) == false)
            return false;

        // Takes priority over Critical/elemental below - same precedence HitFeedback gives this
        // field for its own flash color. Only ever reachable here (dealt by me), never on the
        // taken-by-me branch above - FrontalDamageReduction only ever applies to an Enemy target,
        // and the local player is never one.
        if (e.FrontalReduced == true)
        {
            kind = DamageNumberKind.FrontalReducedDealtByMe;
            return true;
        }

        // No Critical variant of Burn/a reaction proc - both always bypass crit resolution (see
        // DamageUtility.ApplyDamage), so e.IsCritical is never true alongside a non-Neutral Element.
        kind = e.IsCritical ? DamageNumberKind.CriticalDealtByMe : ResolveElementalKind(e.Element, e.ReactionProc, taken: false);
        return true;
    }

    // Traces a hit involving a Sentry chassis or one of its barrels back to the player who deployed
    // it - neither carries a PlayerLink of its own (see Sentry.qtn's own Owner comment), so a plain
    // entity == localPlayer check would never recognize "my sentry got hit" or "my sentry's barrel
    // dealt this" as being about me. Returns the entity itself unchanged for anything that isn't a
    // Sentry/SentryBarrel (a real player, an enemy, a projectile, ...).
    private static EntityRef ResolveOwningPlayer(Frame frame, EntityRef entity)
    {
        if (frame == null || entity == EntityRef.None)
            return entity;

        if (frame.TryGet<SentryBarrel>(entity, out var barrel) == true)
            return ResolveOwningPlayer(frame, barrel.Sentry);

        if (frame.TryGet<Sentry>(entity, out var sentry) == true)
            return sentry.Owner;

        return entity;
    }

    // Whether this hit came OUT of a sentry - the chassis itself (its Overload detonation) or one
    // of its barrels (ordinary gunfire). Checked against the raw event Owner rather than
    // ResolveOwningPlayer's result, which has already traced past both to the deploying player.
    private static bool IsSentrySource(Frame frame, EntityRef entity)
    {
        if (frame == null || entity == EntityRef.None)
            return false;

        return frame.Has<SentryBarrel>(entity) == true || frame.Has<Sentry>(entity) == true;
    }

    // reactionProc (EventEntityDamaged.ReactionProc) is what actually separates Thermal Shock from a
    // plain Burn tick here - both tag ElementType.Fire, so Element alone can't tell them apart (see
    // that field's own comment in Events.qtn). Lightning has no periodic-tick source of its own today
    // (Electrified's Jolt only Staggers, never damages - see StatusEffectSystem.TickElectrified), so
    // every Lightning-tagged hit is already an Overload proc regardless of reactionProc; it's read
    // here anyway for symmetry/future-proofing rather than assuming that stays true forever. Ice
    // (Shatter) has no dedicated kind yet - falls through to the plain default below.
    private static DamageNumberKind ResolveElementalKind(ElementType element, bool reactionProc, bool taken)
    {
        switch (element)
        {
            case ElementType.Fire:
                if (reactionProc == true)
                    return taken ? DamageNumberKind.ThermalShockTakenByMe : DamageNumberKind.ThermalShockDealtByMe;
                return taken ? DamageNumberKind.BurnTakenByMe : DamageNumberKind.BurnDealtByMe;

            case ElementType.Lightning:
                return taken ? DamageNumberKind.OverloadTakenByMe : DamageNumberKind.OverloadDealtByMe;

            default:
                return taken ? DamageNumberKind.TakenByMe : DamageNumberKind.DealtByMe;
        }
    }

    private void OnEntityHealed(EventEntityHealed e)
    {
        if (TryResolveHealKind(e, out var kind) == false)
            return;

        if (TryResolveHealPosition(e, out var worldPosition) == false)
            return;

        Spawn(kind, e.Amount.AsFloat, worldPosition);
    }

    // Being healed is checked before dealing one, same reasoning as TryResolveKind - a self-heal
    // reads as healing on me rather than healing I dealt. Unlike TryResolveKind (damage), this
    // never actually returns false - a heal is worth seeing for every nearby player, not just one
    // the local player was a party to (see HealedAlly's own comment). Keeps the bool-out-param
    // shape anyway, for symmetry with every other resolver here.
    private bool TryResolveHealKind(EventEntityHealed e, out DamageNumberKind kind)
    {
        // Enemy heals (e.g. FlyingShielder topping up an ally) get their own dedicated kind rather
        // than falling through to HealedAlly below - same event, different flavor/color.
        Frame frame = e.Game.Frames.Predicted;
        if (frame != null && frame.Has<Enemy>(e.Target) == true)
        {
            kind = DamageNumberKind.HealedEnemy;
            return true;
        }

        if (IsLocalEntity(e.Target))
        {
            kind = DamageNumberKind.HealedTakenByMe;
            return true;
        }

        if (IsLocalEntity(e.Owner))
        {
            kind = DamageNumberKind.HealedDealtByMe;
            return true;
        }

        kind = DamageNumberKind.HealedAlly;
        return true;
    }

    // Membership check across every registered local slot (not equality with a single EntityRef),
    // so couch co-op's second local player also reads their own heals/shields/damage as "me".
    private static bool IsLocalEntity(EntityRef entity)
    {
        return MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.IsLocalEntity(entity);
    }

    private bool TryResolveHealPosition(EventEntityHealed e, out Vector3 worldPosition)
    {
        worldPosition = default;

        var frame = e.Game.Frames.Predicted;
        if (frame == null || frame.Has<Transform3D>(e.Target) == false)
            return false;

        worldPosition = frame.Get<Transform3D>(e.Target).Position.ToUnityVector3() + worldOffset;
        return true;
    }

    // Shield counterpart to OnEntityHealed/TryResolveHealKind/TryResolveHealPosition above - same
    // shape, same Taken/Dealt/Enemy precedence, just off EventEntityShielded.
    private void OnEntityShielded(EventEntityShielded e)
    {
        if (TryResolveShieldKind(e, out var kind) == false)
            return;

        if (TryResolveShieldPosition(e, out var worldPosition) == false)
            return;

        Spawn(kind, e.Amount.AsFloat, worldPosition);
    }

    // Same "never actually returns false" shape as TryResolveHealKind - see that method's comment.
    private bool TryResolveShieldKind(EventEntityShielded e, out DamageNumberKind kind)
    {
        Frame frame = e.Game.Frames.Predicted;
        if (frame != null && frame.Has<Enemy>(e.Target) == true)
        {
            kind = DamageNumberKind.ShieldedEnemy;
            return true;
        }

        if (IsLocalEntity(e.Target))
        {
            kind = DamageNumberKind.ShieldedTakenByMe;
            return true;
        }

        if (IsLocalEntity(e.Owner))
        {
            kind = DamageNumberKind.ShieldedDealtByMe;
            return true;
        }

        kind = DamageNumberKind.ShieldedAlly;
        return true;
    }

    private bool TryResolveShieldPosition(EventEntityShielded e, out Vector3 worldPosition)
    {
        worldPosition = default;

        var frame = e.Game.Frames.Predicted;
        if (frame == null || frame.Has<Transform3D>(e.Target) == false)
            return false;

        worldPosition = frame.Get<Transform3D>(e.Target).Position.ToUnityVector3() + worldOffset;
        return true;
    }

    private void Spawn(DamageNumberKind kind, float damage, Vector3 worldPosition)
    {
        DamageNumberStyle style = FindStyle(kind);
        if (style == null)
            return;

        DamageNumberUiWidget widget = _pool.Get();
        widget.Play(style, damage, worldPosition, ResolveBurstStaggerDelay(), _pool.Release);
    }

    // Numbers landing in the same Unity frame get an increasing spawn delay instead of all
    // animating from t=0 - resets as soon as a frame passes with no spawns.
    private float ResolveBurstStaggerDelay()
    {
        if (Time.frameCount != _burstFrame)
        {
            _burstFrame = Time.frameCount;
            _burstIndex = 0;
        }

        float delay = Mathf.Min(_burstIndex * burstStaggerStep, maxBurstStaggerDelay);
        _burstIndex++;
        return delay;
    }

    private DamageNumberStyle FindStyle(DamageNumberKind kind)
    {
        foreach (var style in styles)
        {
            if (style.Kind == kind)
                return style;
        }

        LogHelper.Error("DamageFeedback", $"No style configured for {kind} - add a row to {name}'s styles list.");
        return null;
    }

    private ObjectPool<DamageNumberUiWidget> CreatePool()
    {
        return new ObjectPool<DamageNumberUiWidget>(
            createFunc: CreateWidget,
            actionOnGet: widget => widget.gameObject.SetActive(true),
            actionOnRelease: widget => widget.gameObject.SetActive(false),
            actionOnDestroy: widget => Destroy(widget.gameObject));
    }

    private DamageNumberUiWidget CreateWidget()
    {
        var widget = Instantiate(widgetPrefab, widgetParent);
        widget.Setup(_canvas, _worldCamera);
        return widget;
    }

    private void Prewarm(int count)
    {
        var buffer = new DamageNumberUiWidget[count];
        for (int i = 0; i < count; i++)
            buffer[i] = _pool.Get();
        for (int i = 0; i < count; i++)
            _pool.Release(buffer[i]);
    }

    [Button]
    public void TestDamageTakenByMe() => SpawnOnLocalPlayer(DamageNumberKind.TakenByMe);

    [Button]
    public void TestDamageDealtByMe() => SpawnOnLocalPlayer(DamageNumberKind.DealtByMe);

    [Button]
    public void TestCriticalDealtByMe() => SpawnOnLocalPlayer(DamageNumberKind.CriticalDealtByMe);

    [Button]
    public void TestHealedTakenByMe() => SpawnOnLocalPlayer(DamageNumberKind.HealedTakenByMe);

    [Button]
    public void TestHealedDealtByMe() => SpawnOnLocalPlayer(DamageNumberKind.HealedDealtByMe);

    private void SpawnOnLocalPlayer(DamageNumberKind kind)
    {
        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalPlayerSetup == false)
            return;

        Spawn(kind, UnityEngine.Random.Range(5, 200),
            MyLocalPlayer.Instance._localPlayerView.transform.position + worldOffset);
    }
}
