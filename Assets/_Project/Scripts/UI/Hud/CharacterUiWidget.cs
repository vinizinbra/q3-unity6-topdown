using System.Collections;
using NaughtyAttributes;
using Photon.Client.StructWrapping;
using Photon.Deterministic;
using Quantum;
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
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2f, 0f);

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

    [Header("Weapon")]
    [SerializeField] private Slider ammoSlider;
    [SerializeField, Tooltip("Fills as the reload runs and hides once it lands, so it reads as \"time until a full magazine\" rather than a second ammo bar.")]
    private Slider reloadSlider;
    [SerializeField, Tooltip("Punch-scaled when the weapon finishes reloading (timed or instant). Left unassigned to skip the effect.")]
    private RectTransform reloadPunchTarget;
    [SerializeField] private float reloadPunchScale = 1.25f;
    [SerializeField] private float reloadPunchDuration = 0.25f;

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
    [SerializeField, Tooltip("Shown on whoever is CURRENTLY benefiting from a Guardian-ascended Brute's Protector Aura (any player, not just Brute himself) - StatusEffects.GuardianDamageReductionRemaining, its own dedicated field so it can't collide with Max's Too Angry to Die.")]
    private StatusIndicator guardianAuraIndicator;
    [SerializeField, Tooltip("Shown on Brute himself while his Juggernaut Hero Skill is actively channeling (JuggernautCharge component present) - that's when CharacterStats.DamageReduction is temporarily boosted, see JuggernautSkillData.Begin/End.")]
    private StatusIndicator juggernautChannelIndicator;

    private Canvas _canvas;
    private Camera _worldCamera;
    private QuantumGame _game;
    private EntityRef _entityRef;
    private Transform _followTarget;
    private Coroutine _reloadPunchRoutine;
    private Coroutine _shieldShineRoutine;
    private bool _shieldWasRecharging;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public void Setup(QuantumGame game, EntityRef entityRef, Transform followTarget, string displayName = null)
    {
        _game = game;
        _entityRef = entityRef;
        _followTarget = followTarget;
        _worldCamera = Camera.main;

        SetShown(nameText, string.IsNullOrEmpty(displayName) == false);
        if (nameText != null)
            nameText.text = displayName;

        QuantumEvent.Subscribe<EventWeaponReloaded>(this, OnWeaponReloaded);
    }

    private void OnWeaponReloaded(EventWeaponReloaded e)
    {
        if (e.Entity != _entityRef)
            return;

        PunchReloadScale();
    }

    [Button]
    private void PunchReloadScale()
    {
        if (reloadPunchTarget == null)
            return;

        if (_reloadPunchRoutine != null)
            StopCoroutine(_reloadPunchRoutine);

        _reloadPunchRoutine = StartCoroutine(ReloadPunchRoutine());
    }

    private IEnumerator ReloadPunchRoutine()
    {
        float peakDuration = reloadPunchDuration * 0.35f;
        float settleDuration = reloadPunchDuration - peakDuration;

        yield return AnimateScale(1f, reloadPunchScale, peakDuration);
        yield return AnimateScale(reloadPunchScale, 1f, settleDuration);

        _reloadPunchRoutine = null;
    }

    private IEnumerator AnimateScale(float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            reloadPunchTarget.localScale = Vector3.one * Mathf.SmoothStep(from, to, elapsed / duration);
            yield return null;
        }

        reloadPunchTarget.localScale = Vector3.one * to;
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

        UpdateHealth(frame);
        UpdateShield(frame);
        UpdateWeapon(frame);
        UpdateStatusEffects(frame);
        UpdateExplodeOnDeath(frame);
        UpdateRevengeMark(frame);
    }

    private void FollowTarget()
    {
        if (UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, _followTarget.position + worldOffset, out var anchoredPosition))
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
        bool shown = hasStatus && status.GuardianDamageReductionRemaining > FP._0;
        guardianAuraIndicator.SetShown(shown);

        if (shown)
            guardianAuraIndicator.SetTimer($"{status.GuardianDamageReductionRemaining.AsFloat:F1}s");
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
    // show "you are marked" without any cross-entity lookup.
    private void UpdateRevengeMark(Frame frame)
    {
        bool shown = frame.TryGet<RevengeMark>(_entityRef, out var mark) && mark.RemainingDuration > FP._0;
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

    private static void SetShown(Component component, bool shown)
    {
        if (component == null || component.gameObject.activeSelf == shown)
            return;

        component.gameObject.SetActive(shown);
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
            if (root != null && root.activeSelf != shown)
                root.SetActive(shown);
        }

        public void SetTimer(string text)
        {
            if (timerText != null)
                timerText.text = text;
        }
    }
}
