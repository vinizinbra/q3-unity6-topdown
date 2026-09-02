using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using Photon.Client.StructWrapping;
using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen-space HUD widget (health, shield, ammo, reload) that follows a character entity's world
// position. Lives under CharacterUiWidgetManager's widgetParent, not under the character's own view
// prefab - keeps it out of the world rig hierarchy that BlobAnimationView squashes/rotates for
// animation.
//
// One prefab serves players and enemies, so every bar past health is optional twice over: its
// Slider and value label may be unassigned on a given prefab, and the component behind it may be
// absent on a given entity (an enemy carries no Weapon, an unshielded one no Shield). Each bar
// hides itself rather than the widget assuming a loadout.
//
// Every bar here is a plain "read the component, set the value" - a trailing "recent damage" bar on
// any of them is DelayedSliderWidget's job, sitting on the trailing bar itself and watching this
// one's slider.
//
// Status effect indicators follow the same rule, just with a StatusIndicator (root + timer text)
// instead of a Slider - each shows itself only while its own Remaining timer is above zero, same
// optional-per-entity pattern as Shield/Weapon. Most read that timer off StatusEffects;
// explodeOnDeathIndicator is the one exception, reading the standalone ExplodeOnDeath component
// instead (see UpdateExplodeOnDeath).
public class CharacterUiWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField, Tooltip("Shared base offset applied to every widget (players and enemies). A per-character nudge is added on top via Setup - hand-authored per hero (CharView.widgetOffset) or derived from the collider radius per enemy (EnemyView.widgetRadiusOffsetMultiplier).")]
    private Vector3 worldOffset = new Vector3(0f, 2f, 0f);

    [Header("Name")]
    [SerializeField, Tooltip("Left unassigned to skip name display. Hidden whenever Setup's displayName is null/empty - e.g. EnemyView only passes one for tiers above Filler.")]
    private TMP_Text nameText;

    [Header("Health")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthText;

    [Header("Shield")]
    [SerializeField, Tooltip("Shown only while the entity carries a Shield component with a Max above zero.")]
    private Slider shieldSlider;
    [SerializeField] private TMP_Text shieldText;
    [SerializeField, Tooltip("Fill graphic of shieldSlider; flashes this color when the shield starts recharging. Auto-resolved from shieldSlider.fillRect if left unassigned.")]
    private Image shieldFillImage;
    [SerializeField] private Color shieldShineColor = Color.white;
    [SerializeField] private float shieldShineDuration = 0.4f;

    [SerializeField, Tooltip("Only relevant for a TEMPORARY shield (Shield.TemporaryDuration > 0, e.g. Brute's Juggernaut) - once ExpirationRemaining drops to/below this many seconds, the fill starts pulsing toward shieldWarningColor so it's visually obvious the Shield is about to disappear entirely, not just draining. Meaningless (never triggers) for a plain persistent charge-only shield or a classically recharging one, since both always read TemporaryDuration 0.")]
    private float shieldWarningThreshold = 1.5f;
    [SerializeField] private Color shieldWarningColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField, Tooltip("Pulse speed while warning, in radians/sec.")]
    private float shieldWarningPulseSpeed = 6f;

    [Header("Weapon")]
    [SerializeField] private Slider ammoSlider;
    [SerializeField, Tooltip("Fills as the reload runs and hides once it lands, so it reads as \"time until a full magazine\" rather than a second ammo bar.")]
    private Slider reloadSlider;
    [SerializeField, Tooltip("Punch-scaled the moment the magazine comes back to FULL - a timed reload landing, an instant reload, or a mid-combat refill (Max's Full Throttle, Run & Gun). Left unassigned to skip the effect.")]
    private RectTransform reloadPunchTarget;
    [SerializeField, Tooltip("Punch strength per axis, ADDED to the target's own scale. Deliberately non-uniform and Y-heavy: this target is a horizontal bar, so stretching it vertically reads as a punch, while stretching it horizontally just reads as the bar briefly getting longer - i.e. as more ammo, which is exactly the wrong signal. Keep X small and Z at 0.")]
    private Vector3 reloadPunchStrength = new Vector3(0.15f, 1f, 0f);
    [SerializeField] private float reloadPunchDuration = 0.45f;
    [SerializeField, Tooltip("Punch oscillations per second. LOWER reads as one big deliberate swell (best when you want the punch to feel heavy); higher reads snappy and buzzy. Turn this DOWN, not up, to make a punch feel bigger.")]
    private float reloadPunchFrequency = 7f;

    [Header("Status Effects")]
    [SerializeField] private StatusIndicator burnIndicator;
    [SerializeField, Tooltip("Rift Mark does nothing by itself - timerText shows the target's current stack count as \"xN\" (not a countdown), priming it for whichever elemental reaction (Detonation/Deep Freeze/Rupture/Overload/Singularity) lands next. See docs/elemental-reactions.md.")]
    private StatusIndicator riftMarkIndicator;
    [SerializeField] private StatusIndicator iceIndicator;
    [SerializeField, Tooltip("Ice+RiftMark's Deep Freeze reaction - stretches the entity's own attack anticipation/windup (StatusEffectUtility.GetAnticipationMultiplier), not a lockout, so it's shown separately from Stun. See docs/elemental-reactions.md.")]
    private StatusIndicator deepFreezeIndicator;
    [SerializeField] private StatusIndicator stunIndicator;
    [SerializeField, Tooltip("Root pins movement only (the entity can still attack), unlike Stun which freezes everything - shown separately so both can be visible at once if somehow both are active.")]
    private StatusIndicator rootIndicator;
    [SerializeField] private StatusIndicator ruptureIndicator;
    [SerializeField, Tooltip("Mirror of Rupture - reduces the entity's own outgoing damage instead of incoming. Applied to enemies by Brute's Protector Aura.")]
    private StatusIndicator intimidateIndicator;
    [SerializeField, Tooltip("Shown while the entity carries ExplodeOnDeath (see DamageUtility.TryMarkExplodeOnDeath) - a separate component from StatusEffects, not the damage-multiplier Rupture above despite the similar name.")]
    private StatusIndicator explodeOnDeathIndicator;
    [SerializeField, Tooltip("Shown on this entity while it is marked by a Vendetta holder's RevengeMark (any number of enemies can carry one simultaneously) - a separate component from StatusEffects, same shape as explodeOnDeathIndicator above. See docs/max-vendetta-fire-mastery.md.")]
    private StatusIndicator revengeMarkIndicator;

    [Header("Defense States (Brute)")]
    [SerializeField, Tooltip("Shown on whoever is CURRENTLY benefiting from any continuous damage-reduction aura (a Guardian-ascended Brute's Protector Aura, a Fire Support Lux Sentry) - StatusEffects.AuraDamageReductionRemaining, the one shared aura-DR slot, so it can't collide with Max's Too Angry to Die.")]
    private StatusIndicator guardianAuraIndicator;
    [SerializeField, Tooltip("Shown on Brute himself while his Juggernaut Hero Skill is actively channeling (JuggernautCharge component present) - that's when CharacterStats.DamageReduction is temporarily boosted, see JuggernautSkillData.Begin/End.")]
    private StatusIndicator juggernautChannelIndicator;

    [Header("Hero Resources")]
    [Header("Accessory Guard")]
    [SerializeField, Tooltip("Shown only while this entity's Signature Accessory is actually WORN (AccessoryGuard.State == Equipped) - hidden while it's flying, lying in the level, or broken. Left unassigned to skip. See docs/accessory-guard.md.")]
    private GameObject accessoryEquippedRoot;

    [SerializeField, Tooltip("The whole pip strip - its empty frames/backing included. Hidden outright for any entity that has no AccessoryGuard at all (every enemy, every sentry), since the pips themselves going dark would otherwise leave a row of empty guard slots on a thing that can never have one. Left unassigned to skip.")]
    private GameObject accessoryGuardRoot;

    [SerializeField, Tooltip("A SINGLE pip used as a template - one instance is spawned per point of MaxDurability at runtime, so the strip adapts to whatever a player's max actually is (Glass Core doubles it, Last Bastion drops it to 0). Deactivated automatically and never shown itself. Each spawned pip stays active for its whole life (it IS the slot); only its own available/spent children swap as durability is spent. Assign this rather than the fixed array below.")]
    private AccessoryGuardPipWidget accessoryGuardPipTemplate;

    [SerializeField, Tooltip("Where spawned pips are parented. Optional - defaults to accessoryGuardPipTemplate's own parent, which is right for a strip authored as template-inside-its-own-row.")]
    private Transform accessoryGuardPipContainer;

    [SerializeField, Tooltip("LEGACY fallback, used only when no pip template is assigned: a fixed array of hand-authored pips, deactivated from the right (index i shown while i < CurrentDurability). Cannot represent a MaxDurability higher than the number of objects authored here, which is why the template above is preferred.")]
    private GameObject[] accessoryGuardPips;

    [Header("Free Hit Guard")]
    [SerializeField, Tooltip("Shown only while this entity has a Free Hit Guard running (StatusEffects.FreeHitGuardRemaining - granted by Brute's Bodyguard today). Left unassigned to skip. Self-hides for every entity that has none, so the one shared prefab keeps serving players and enemies alike.")]
    private GameObject freeHitGuardRoot;

    [SerializeField, Tooltip("Image with Image Type = Filled, drained as the guard's timer runs down (1 = just granted, 0 = about to lapse). Needs StatusEffects.FreeHitGuardDuration as its denominator, which is exactly why the simulation stores it - every OTHER timed status here only shows a countdown NUMBER, so this is the first one that had to know what 'full' was.")]
    private Image freeHitGuardFill;

    [SerializeField, Tooltip("Per-hero resource readouts (Brute/Max/Zara/Lux) authored as children of this widget - left empty, auto-populated via GetComponentsInChildren in Setup. Each one self-hides unless the entity this widget follows actually carries that hero's own components, so the single shared prefab keeps serving every hero AND every enemy. This is the only place they live: the party HUD deliberately shows none of them.")]
    private HeroHudWidget[] heroWidgets;

    private Canvas _canvas;
    private Camera _worldCamera;
    private QuantumGame _game;
    private EntityRef _entityRef;

    // One-shot so a pip/MaxDurability mismatch is reported once per widget rather than every frame.
    private bool _warnedPipShortfall;

    // Live pip instances spawned from accessoryGuardPipTemplate, rebuilt only when MaxDurability
    // changes (see RebuildAccessoryPips). Empty on the legacy hand-authored-array path.
    private readonly List<AccessoryGuardPipWidget> _spawnedAccessoryPips = new List<AccessoryGuardPipWidget>();
    private Transform _followTarget;
    private Vector3 _characterOffset;
    private Tween _reloadPunchTween;
    private Coroutine _shieldShineRoutine;
    private bool _shieldWasRecharging;
    private Color _shieldBaseFillColor = Color.white;
    private bool _shieldBaseFillColorCaptured;
    private CanvasGroup _selfCanvasGroup;

    // Anchor for a screen-space element that needs to point at THIS ENTITY'S HEALTH BAR
    // specifically (e.g. TargetArrowWidget) rather than the widget as a whole - falls back to
    // selfRect if healthSlider was ever left unassigned.
    public RectTransform HealthAnchor => healthSlider != null ? (RectTransform)healthSlider.transform : selfRect;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    // The manager's widgetPrefab is a disabled scene object (see CharacterUiWidgetManager.Awake) -
    // clones stay inactive until SetActive(true) right after this call, so Unity defers their Awake()
    // until then. Setup runs first and only once per instance, so anything FollowTarget needs has to
    // be resolved here rather than in Awake - by the time Awake finally fires, it may already be too late.
    public void Setup(QuantumGame game, EntityRef entityRef, Transform followTarget, string displayName = null, Vector3 characterOffset = default)
    {
        _game = game;
        _entityRef = entityRef;
        _followTarget = followTarget;
        _characterOffset = characterOffset;
        _worldCamera = Camera.main;

        if (heroWidgets == null || heroWidgets.Length == 0)
            heroWidgets = GetComponentsInChildren<HeroHudWidget>(true);

        SetShown(nameText, string.IsNullOrEmpty(displayName) == false);
        if (nameText != null)
            nameText.text = displayName;

        QuantumEvent.Subscribe<EventWeaponReloaded>(this, OnWeaponReloaded);
    }

    // Drops the trailing "recent damage" bars on every slider of this widget, so its readout tracks
    // the simulation with no lag at all. Opt-in per spawner rather than per prefab, since one prefab
    // serves players, enemies and sentries alike - a sentry's bar is a small, short-lived thing the
    // owner reads at a glance, where a trail reads as the widget being wrong rather than as drama.
    public void SetBarsInstant(bool instant)
    {
        foreach (var trailingBar in GetComponentsInChildren<DelayedSliderWidget>(true))
            trailingBar.SetInstant(instant);
    }

    private void OnWeaponReloaded(EventWeaponReloaded e)
    {
        if (e.Entity != _entityRef)
            return;

        PunchReloadScale();
    }

    // Replaces a hand-rolled coroutine that lerped a UNIFORM Vector3.one * scale out and back with
    // SmoothStep. Two things were wrong with that for a bar: it scaled X as much as Y (so the bar
    // momentarily read as being longer, i.e. as MORE ammo, fighting the thing it was trying to
    // celebrate), and a symmetric out-and-back ease has no snap to it. PrimeTween's PunchScale is
    // per-axis and springy, and is already this project's idiom for exactly this (CurrencyUiWidget,
    // JuicyEffects) - so this is one call instead of two coroutines.
    //
    // Unscaled time, same as CurrencyUiWidget's own punch: a reload landing during a hit-stop freeze
    // (HurtOverlayUiWidget) should still play rather than being held mid-punch.
    [Button]
    private void PunchReloadScale()
    {
        if (reloadPunchTarget == null)
            return;

        // Stopped rather than left to overlap - PunchScale is relative to the target's scale when it
        // starts, so a second punch landing mid-punch would otherwise compound off an already-
        // stretched bar and leave it permanently the wrong size.
        _reloadPunchTween.Stop();
        _reloadPunchTween = Tween.PunchScale(reloadPunchTarget, reloadPunchStrength, reloadPunchDuration,
            reloadPunchFrequency, useUnscaledTime: true);
    }

    private void LateUpdate()
    {
        if (_game == null || _followTarget == null)
            return;

        FollowTarget();

        Frame frame = _game.Frames.Predicted;

        // CharView/EnemyView despawn this widget from their DeInitialize, which lands a frame or
        // more after the sim destroyed the entity - reading components in that window throws.
        if (frame.Exists(_entityRef) == false)
            return;

        // Hidden for the same window PlayerFallSystem/EnemyFallSystem hide the character/enemy
        // sprite itself (see LevelConfig.FallRespawnDelay) - a health bar hovering with nothing
        // visible underneath it reads just as broken as a floating gun. A CanvasGroup rather than
        // SetActive on this widget's own GameObject, which would stop THIS LateUpdate from running
        // and leave it stuck hidden forever - same "must stay active to self-heal" reasoning
        // BlobAnimationView's bodyRoot/handsRoot fields already document.
        bool isFallPending = FallStateUtility.IsFallPending(frame, _entityRef);
        ApplySelfHidden(isFallPending);

        if (isFallPending == true)
            return;

        UpdateHealth(frame);
        UpdateShield(frame);
        UpdateWeapon(frame);
        UpdateStatusEffects(frame);
        UpdateExplodeOnDeath(frame);
        UpdateRevengeMark(frame);
        UpdateAccessoryGuard(frame);
        UpdateFreeHitGuard(frame);
        UpdateHeroWidgets(frame);
    }

    // Recoverable Accessory Guard readout (see docs/accessory-guard.md) - an "is it currently worn"
    // marker plus one pip per remaining durability point. Both are plain SetActive swaps off the
    // simulation's own AccessoryGuard, the same idiom every other indicator on this widget uses, and
    // both self-hide entirely for any entity that has no guard at all (every enemy, and every hero
    // in a build where RuntimeConfig.AccessoryGuardConfig was never assigned) - so the one shared
    // widget prefab keeps serving players and enemies alike.
    //
    // The pip strip is SPAWNED to match MaxDurability rather than hand-authored, because MaxDurability
    // is not a constant: Glass Core doubles it (3 -> 6) mid-run, and Last Bastion drops it to 0. A
    // fixed array can't represent either, so the strip is rebuilt whenever that number changes and
    // then only toggled per pip as durability is spent - index i shown while i < CurrentDurability.
    //
    // Rebuild is gated on MaxDurability actually changing, so the common case (durability going up and
    // down within the same max) is just a few SetActive calls, not a respawn. Same
    // destroy-and-reinstantiate-from-a-deactivated-template idiom PartyHistoryUpgradeContainer uses.
    private void UpdateAccessoryGuard(Frame frame)
    {
        bool hasGuard = frame.TryGet<AccessoryGuard>(_entityRef, out var guard) && guard.MaxDurability > 0;

        if (accessoryGuardRoot != null && accessoryGuardRoot.activeSelf != hasGuard)
            accessoryGuardRoot.SetActive(hasGuard);

        if (accessoryEquippedRoot != null)
            accessoryEquippedRoot.SetActive(hasGuard && guard.State == AccessoryGuardState.Equipped);

        int maxDurability = hasGuard ? guard.MaxDurability : 0;
        int currentDurability = hasGuard ? guard.CurrentDurability : 0;

        if (accessoryGuardPipTemplate != null)
        {
            UpdateSpawnedAccessoryPips(maxDurability, currentDurability);
            return;
        }

        UpdateAuthoredAccessoryPips(hasGuard, maxDurability, currentDurability);
    }

    // Two different quantities drive the strip, which is why the pip is its own widget:
    //   - how many pips EXIST tracks MaxDurability (rebuilt only when that changes)
    //   - whether each one reads as available tracks CurrentDurability (every frame, cheap)
    // A spent pip therefore leaves its empty frame behind instead of vanishing, so the player can
    // still see how much durability a full repair would give back.
    private void UpdateSpawnedAccessoryPips(int maxDurability, int currentDurability)
    {
        if (_spawnedAccessoryPips.Count != maxDurability)
            RebuildAccessoryPips(maxDurability);

        for (int i = 0; i < _spawnedAccessoryPips.Count; i++)
        {
            if (_spawnedAccessoryPips[i] == null)
                continue;

            _spawnedAccessoryPips[i].SetAvailable(i < currentDurability);
        }
    }

    private void RebuildAccessoryPips(int maxDurability)
    {
        // The template is never shown itself, so it can be authored visible in the prefab for
        // editing convenience and still not leak an extra pip at runtime.
        if (accessoryGuardPipTemplate.gameObject.activeSelf == true)
            accessoryGuardPipTemplate.gameObject.SetActive(false);

        for (int i = 0; i < _spawnedAccessoryPips.Count; i++)
        {
            if (_spawnedAccessoryPips[i] != null)
                Destroy(_spawnedAccessoryPips[i].gameObject);
        }

        _spawnedAccessoryPips.Clear();

        Transform container = accessoryGuardPipContainer != null
            ? accessoryGuardPipContainer
            : accessoryGuardPipTemplate.transform.parent;

        for (int i = 0; i < maxDurability; i++)
        {
            AccessoryGuardPipWidget pip = Instantiate(accessoryGuardPipTemplate, container);

            // The pip itself is the SLOT and stays visible; SetAvailable (called by the caller right
            // after this) is what decides whether it reads as filled or empty.
            pip.gameObject.SetActive(true);
            _spawnedAccessoryPips.Add(pip);
        }
    }

    // Legacy path for a widget still authored with a fixed pip array. Kept so an un-rewired scene
    // keeps working, but it cannot grow past what was authored - hence the one-time warning.
    private void UpdateAuthoredAccessoryPips(bool hasGuard, int maxDurability, int currentDurability)
    {
        if (accessoryGuardPips == null)
            return;

        for (int i = 0; i < accessoryGuardPips.Length; i++)
        {
            if (accessoryGuardPips[i] == null)
                continue;

            bool shown = i < currentDurability;

            if (accessoryGuardPips[i].activeSelf != shown)
                accessoryGuardPips[i].SetActive(shown);
        }

        if (hasGuard == true && _warnedPipShortfall == false && accessoryGuardPips.Length < maxDurability)
        {
            _warnedPipShortfall = true;
            LogHelper.Warn("Accessory", $"{name} has {accessoryGuardPips.Length} hand-authored accessory guard pip(s) but " +
                $"MaxDurability is {maxDurability} - the readout will under-report remaining guards. " +
                "Assign accessoryGuardPipTemplate instead, which spawns the strip to fit.", this);
        }
    }

    // Every hero widget is refreshed off the frame already resolved above rather than reading one
    // of its own - they have no Quantum callback and no local-player binding, since this widget
    // already knows exactly which entity it follows (see HeroHudWidget).
    // Free Hit Guard readout - "you currently have a free block banked, and here's how long it lasts."
    //
    // This matters more than a typical status icon because Bodyguard is a GRANTED buff: without a
    // readout, a teammate has no way to know Brute gave them anything, and the ability is invisible to
    // the very person it protects until it silently saves them. The fill is the point - a guard that
    // lapses unused should be visibly running out, so there's a reason to go spend it.
    //
    // Unlike every other timed status on this widget (which show a countdown number via
    // StatusIndicator.timerText and therefore need no denominator), a fill has to know what "full"
    // was - hence StatusEffects.FreeHitGuardDuration. Deriving it View-side by remembering the largest
    // Remaining seen would break the moment a longer guard refreshes a shorter one.
    private void UpdateFreeHitGuard(Frame frame)
    {
        bool active = frame.TryGet<StatusEffects>(_entityRef, out var status) && status.FreeHitGuardRemaining > FP._0;

        if (freeHitGuardRoot != null && freeHitGuardRoot.activeSelf != active)
            freeHitGuardRoot.SetActive(active);

        if (active == false || freeHitGuardFill == null)
            return;

        // Duration is only ever 0 here if a guard was somehow applied without going through
        // ApplyFreeHitGuard - show a full bar rather than an empty one, so the readout fails toward
        // "you have a guard" (which is true) instead of "it's about to expire" (which isn't).
        freeHitGuardFill.fillAmount = status.FreeHitGuardDuration > FP._0
            ? Mathf.Clamp01((status.FreeHitGuardRemaining / status.FreeHitGuardDuration).AsFloat)
            : 1f;
    }

    private void UpdateHeroWidgets(Frame frame)
    {
        if (heroWidgets == null)
            return;

        foreach (HeroHudWidget widget in heroWidgets)
        {
            if (widget != null)
                widget.Refresh(frame, _entityRef);
        }
    }

    // Lazily adds a CanvasGroup to selfRect the first time it's needed, so no prefab needs
    // re-authoring for this to work. No-op if selfRect was never assigned - same "left unassigned
    // to skip" convention every other optional field here uses.
    private void ApplySelfHidden(bool hidden)
    {
        if (selfRect == null)
            return;

        if (_selfCanvasGroup == null)
            _selfCanvasGroup = selfRect.GetComponent<CanvasGroup>();
        if (_selfCanvasGroup == null)
            _selfCanvasGroup = selfRect.gameObject.AddComponent<CanvasGroup>();

        float targetAlpha = hidden ? 0f : 1f;
        if (Mathf.Approximately(_selfCanvasGroup.alpha, targetAlpha) == true)
            return;

        _selfCanvasGroup.alpha = targetAlpha;
        _selfCanvasGroup.blocksRaycasts = hidden == false;
        _selfCanvasGroup.interactable = hidden == false;
    }

    private void FollowTarget()
    {
        Vector3 widgetPosition = _followTarget.position + worldOffset + _characterOffset;

        // The ammo/reload row is a plain child of selfRect and rides along with it - no world
        // tracking of its own, so its authored layout position inside the widget is what shows.
        if (UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, widgetPosition, out var anchoredPosition))
            selfRect.anchoredPosition = anchoredPosition;
    }

    private void UpdateHealth(Frame frame)
    {
        if (frame.TryGet<Health>(_entityRef, out var health) == false || health.MaxHealth <= FP._0)
            return;

        SetSliderValue(healthSlider, (health.CurrentHealth / health.MaxHealth).AsFloat);
        SetValueText(healthText, health.CurrentHealth, health.MaxHealth);
    }

    private void UpdateShield(Frame frame)
    {
        bool shown = frame.TryGet<Shield>(_entityRef, out var shield) && shield.Max > FP._0;

        SetShown(shieldSlider, shown);
        SetShown(shieldText, shown);

        if (shown == false)
        {
            _shieldWasRecharging = false;
            return;
        }

        SetSliderValue(shieldSlider, (shield.Current / shield.Max).AsFloat);
        SetValueText(shieldText, shield.Current, shield.Max);

        // Mirrors ShieldSystem's own recharge condition, so the shine fires exactly on the tick
        // the sim starts adding Current back rather than on an approximation of it.
        bool isRecharging = shield.RechargeTimer <= FP._0 && shield.Current < shield.Max;

        if (isRecharging && _shieldWasRecharging == false)
            ShineShieldFill();

        _shieldWasRecharging = isRecharging;

        UpdateShieldExpirationWarning(shield);
    }

    // Temporary-shield-only (see shieldWarningThreshold's own tooltip) - pulses the fill toward
    // shieldWarningColor once the single shared expiration timer is about to run out, so "the Shield
    // is about to vanish" reads distinctly from "the Shield is merely low." Yields to the (rarer,
    // shorter) recharge shine above rather than fighting it for the same Image.color.
    private void UpdateShieldExpirationWarning(Shield shield)
    {
        Image fillImage = ResolveShieldFillImage();

        if (fillImage == null || _shieldShineRoutine != null)
            return;

        if (_shieldBaseFillColorCaptured == false)
        {
            _shieldBaseFillColor = fillImage.color;
            _shieldBaseFillColorCaptured = true;
        }

        bool warning = shield.TemporaryDuration > FP._0 && shield.Current > FP._0
            && shield.ExpirationRemaining > FP._0 && shield.ExpirationRemaining.AsFloat <= shieldWarningThreshold;

        if (warning == false)
        {
            fillImage.color = _shieldBaseFillColor;
            return;
        }

        float pulse = (Mathf.Sin(Time.time * shieldWarningPulseSpeed) + 1f) * 0.5f;
        fillImage.color = Color.Lerp(_shieldBaseFillColor, shieldWarningColor, pulse);
    }

    [Button]
    private void ShineShieldFill()
    {
        Image fillImage = ResolveShieldFillImage();

        if (fillImage == null)
            return;

        if (_shieldShineRoutine != null)
            StopCoroutine(_shieldShineRoutine);

        _shieldShineRoutine = StartCoroutine(ShieldShineRoutine(fillImage));
    }

    private Image ResolveShieldFillImage()
    {
        if (shieldFillImage == null && shieldSlider != null && shieldSlider.fillRect != null)
            shieldFillImage = shieldSlider.fillRect.GetComponent<Image>();

        return shieldFillImage;
    }

    private IEnumerator ShieldShineRoutine(Image fillImage)
    {
        Color baseColor = fillImage.color;
        float halfDuration = shieldShineDuration * 0.5f;

        yield return LerpColor(fillImage, baseColor, shieldShineColor, halfDuration);
        yield return LerpColor(fillImage, shieldShineColor, baseColor, halfDuration);

        _shieldShineRoutine = null;
    }

    private static IEnumerator LerpColor(Image image, Color from, Color to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            image.color = Color.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        image.color = to;
    }

    private void UpdateWeapon(Frame frame)
    {
        bool hasWeapon = frame.TryGet<Weapon>(_entityRef, out var weapon);

        UpdateAmmo(weapon, hasWeapon && weapon.MagazineSize > 0);
        UpdateReload(weapon, hasWeapon && weapon.ReloadTimer > FP._0 && weapon.ReloadDuration > FP._0);
    }

    private void UpdateAmmo(Weapon weapon, bool shown)
    {
        SetShown(ammoSlider, shown);

        if (shown)
            SetSliderValue(ammoSlider, Mathf.Clamp01(weapon.Ammo / (float)weapon.MagazineSize));
    }

    // Inverted against ReloadTimer, which counts down - the bar fills toward the magazine landing.
    private void UpdateReload(Weapon weapon, bool shown)
    {
        SetShown(reloadSlider, shown);

        if (shown)
            SetSliderValue(reloadSlider, 1f - (weapon.ReloadTimer / weapon.ReloadDuration).AsFloat);
    }

    private void UpdateStatusEffects(Frame frame)
    {
        bool hasStatus = frame.TryGet<StatusEffects>(_entityRef, out var status);

        UpdateBurn(hasStatus, status);
        UpdateRiftMark(hasStatus, status);
        UpdateIce(hasStatus, status);
        UpdateDeepFreeze(hasStatus, status);
        UpdateStun(hasStatus, status);
        UpdateRoot(hasStatus, status);
        UpdateRupture(hasStatus, status);
        UpdateIntimidate(hasStatus, status);
        UpdateGuardianAura(hasStatus, status);
        UpdateJuggernautChannel(frame);
    }

    private void UpdateBurn(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.BurnRemaining > FP._0;
        burnIndicator.SetShown(shown);

        if (shown)
            burnIndicator.SetTimer($"{status.BurnRemaining.AsFloat:F1}s");
    }

    private void UpdateRiftMark(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.RiftMarkStacks > 0;
        riftMarkIndicator.SetShown(shown);

        if (shown == false)
            return;

        riftMarkIndicator.SetTimer($"x{status.RiftMarkStacks}");
    }

    private void UpdateIce(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.IceRemaining > FP._0;
        iceIndicator.SetShown(shown);

        if (shown)
            iceIndicator.SetTimer($"{status.IceRemaining.AsFloat:F1}s");
    }

    private void UpdateDeepFreeze(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.AnticipationSlowRemaining > FP._0;
        deepFreezeIndicator.SetShown(shown);

        if (shown)
            deepFreezeIndicator.SetTimer($"{status.AnticipationSlowRemaining.AsFloat:F1}s");
    }

    private void UpdateStun(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.StunRemaining > FP._0;
        stunIndicator.SetShown(shown);

        if (shown)
            stunIndicator.SetTimer($"{status.StunRemaining.AsFloat:F1}s");
    }

    private void UpdateRoot(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.RootRemaining > FP._0;
        rootIndicator.SetShown(shown);

        if (shown)
            rootIndicator.SetTimer($"{status.RootRemaining.AsFloat:F1}s");
    }

    private void UpdateRupture(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.RuptureRemaining > FP._0;
        ruptureIndicator.SetShown(shown);

        if (shown)
            ruptureIndicator.SetTimer($"{status.RuptureRemaining.AsFloat:F1}s");
    }

    private void UpdateIntimidate(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.IntimidateRemaining > FP._0;
        intimidateIndicator.SetShown(shown);

        if (shown)
            intimidateIndicator.SetTimer($"{status.IntimidateRemaining.AsFloat:F1}s");
    }

    private void UpdateGuardianAura(bool hasStatus, StatusEffects status)
    {
        bool shown = hasStatus && status.AuraDamageReductionRemaining > FP._0;
        guardianAuraIndicator.SetShown(shown);

        if (shown)
            guardianAuraIndicator.SetTimer($"{status.AuraDamageReductionRemaining.AsFloat:F1}s");
    }

    // Not part of StatusEffects - JuggernautCharge is added at Begin/removed at End (see
    // JuggernautSkillData), so its mere presence already IS the "is this active right now" check, no
    // separate Remaining timer to read.
    private void UpdateJuggernautChannel(Frame frame)
    {
        juggernautChannelIndicator.SetShown(frame.Has<JuggernautCharge>(_entityRef));
    }

    // Own component, not part of StatusEffects - see DamageUtility.TryMarkExplodeOnDeath/ExplodeOnDeath.
    // Remaining now genuinely counts down toward removal (ExplodeOnDeathTimerSystem), so this reads
    // as a real countdown, same as every StatusEffects-backed indicator above.
    private void UpdateExplodeOnDeath(Frame frame)
    {
        bool shown = frame.TryGet<ExplodeOnDeath>(_entityRef, out var explode) && explode.Remaining > FP._0;
        explodeOnDeathIndicator.SetShown(shown);

        if (shown)
            explodeOnDeathIndicator.SetTimer($"{explode.Remaining.AsFloat:F1}s");
    }

    // Own component, not part of StatusEffects - see Vendetta.qtn/MaxVendettaSystem. RevengeMark is
    // the target-side mirror of whichever Max entity currently has this entity marked (RemainingDuration
    // kept in lockstep by MaxVendettaSystem/RevengeMarkTimeoutSystem), so this entity's widget can
    // show "you are marked" without any cross-entity lookup. Only shown to the player whose own Max
    // applied the mark (MarkedBy) - a teammate's Vendetta mark isn't this viewer's business.
    private void UpdateRevengeMark(Frame frame)
    {
        bool shown = frame.TryGet<RevengeMark>(_entityRef, out var mark) && mark.RemainingDuration > FP._0
            && MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.IsLocalEntity(mark.MarkedBy);
        revengeMarkIndicator.SetShown(shown);

        if (shown)
            revengeMarkIndicator.SetTimer($"{mark.RemainingDuration.AsFloat:F1}s");
    }

    // Ceil rather than round, so a surviving sliver of health never reads as a dead "0".
    private static void SetValueText(TMP_Text text, FP current, FP max)
    {
        if (text == null)
            return;

        text.text = $"{Mathf.CeilToInt(current.AsFloat)}/{Mathf.CeilToInt(max.AsFloat)}";
    }

    private static void SetSliderValue(Slider slider, float value)
    {
        if (slider == null)
            return;

        slider.value = value;
    }

    // Routed through TextBatchOptimizer instead of SetActive directly: a label this widget has had
    // hoisted for draw-call batching no longer lives in its own hierarchy, so switching it off here
    // would be undone by the next sync. The optimizer redirects the toggle onto the placeholder left
    // behind in the label's original slot; anything never hoisted is toggled as before.
    private static void SetShown(Component component, bool shown)
    {
        if (component == null)
            return;

        TextBatchOptimizer.SetActive(component.gameObject, shown);
    }

    // One per status type (Burn/RiftMark/Ice/DeepFreeze/Stun/Rupture/Intimidate) - root is whatever the Inspector wires up as
    // that status's visual (icon, background, whatever), shown only while the status is active;
    // timerText is optional, same as every TMP_Text elsewhere in this widget. riftMarkIndicator
    // repurposes timerText to show stack count ("xN") instead of a countdown.
    [System.Serializable]
    private class StatusIndicator
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text timerText;

        public void SetShown(bool shown)
        {
            TextBatchOptimizer.SetActive(root, shown);
        }

        public void SetTimer(string text)
        {
            if (timerText != null)
                timerText.text = text;
        }
    }
}
