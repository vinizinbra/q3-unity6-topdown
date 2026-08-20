using NaughtyAttributes;
using Photon.Deterministic;
using PrimeTween;
using Quantum;
using QuantumUser.View;
using UnityEngine;
using UnityEngine.UI;

// Local-player-only screen-space hurt feedback: a set of Image sprites (assigned in the Inspector -
// e.g. screen-edge frame pieces) that flash red and fade back out whenever the local player's own
// character takes damage. Mirrors HitFeedback's per-entity Flash tween, just driving HUD Images
// instead of a character's SpriteRenderers, and DamageFeedbackManager's local-player gating instead
// of a bound EntityRef - couch co-op's second local player triggers the same shared overlay too
// (MyLocalPlayer.IsLocalEntity is a membership check across every registered local slot).
//
// Also drives a persistent "dying" blink: once ANY local player's own Health drops to/below
// dyingHealthPercent, the overlay loops flashColor<->restColor via an infinite Yoyo tween (same
// cycles:-1/CycleMode.Yoyo idiom SentryView's own continuous damage shake and MovementRingView's
// glow pulse already use) instead of the one-shot Flash a normal hit plays, until healed back
// above the threshold - couch co-op's second local player can trigger/hold this the same way
// OnEntityDamaged above does, checked across every registered slot (MyLocalPlayer.Slots).
public class HurtOverlayUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Screen-edge/frame sprites that flash red on damage.")]
    private Image[] sprites;

    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private Color restColor = new Color(1f, 1f, 1f, 0f);
    [SerializeField] private float duration = 0.25f;

    [Header("Dying (low health)")]
    [SerializeField, Range(0f, 1f), Tooltip("Any local player's own CurrentHealth/MaxHealth at/below this starts the continuous dying blink; recovering above it stops it.")]
    private float dyingHealthPercent = 0.25f;
    [SerializeField, Tooltip("Seconds for one half of the dying blink's flash<->rest cycle - lower reads more urgent.")]
    private float dyingBlinkDuration = 0.4f;

    // One (damage% -> freeze) row of the hit-stop table below.
    [System.Serializable]
    private struct HitStopTier
    {
        [Tooltip("Damage taken as a PERCENT of the hit target's max health (e.g. 10 = 10%).")]
        public float DamagePercent;
        [Tooltip("Screen-freeze duration, in real seconds, when a hit reaches this tier.")]
        public float Duration;
    }

    // Local, presentation-only screen-wide micro-freeze - fires only when THIS client's own local
    // player takes a non-Silent hit (same filter as the flash above), briefly holding
    // Time.timeScale at 0 so the whole view hitches on impact. Safe in Multiplayer: the sim is
    // server-clock authoritative, so timeScale only stalls local prediction/view for a beat and
    // can't desync. Duration comes from the table below, keyed on the hit's damage as a % of max HP.
    [Header("Hit Stop")]
    [SerializeField, Tooltip("Damage%-to-freeze table. A hit uses the highest tier whose DamagePercent it meets or exceeds; a hit below the smallest tier doesn't freeze at all. Rows can be in any order. Leave empty to disable hit-stop.")]
    private HitStopTier[] hitStopTiers =
    {
        new HitStopTier { DamagePercent = 10f, Duration = 0.05f },
        new HitStopTier { DamagePercent = 20f, Duration = 0.10f },
        new HitStopTier { DamagePercent = 40f, Duration = 0.20f },
    };

    private Tween[] _tweens;
    private bool _isDying;

    // Hit-stop state. _preHitStopTimeScale captures whatever owned timeScale before us (normally 1,
    // or a debug slider's value) so we restore to that rather than hardcoding 1; captured once when a
    // freeze begins, not on a re-trigger mid-freeze (e.g. a shotgun's multiple same-tick pellets),
    // which would otherwise capture the frozen 0. Restore ticks from QUpdate - the base's Unity
    // Update() drives it every rendered frame regardless of timeScale, so it still fires while frozen.
    private bool _hitStopActive;
    private float _hitStopResumeAt;
    private float _preHitStopTimeScale = 1f;

    private void Awake()
    {
        _tweens = new Tween[sprites.Length];
        for (var i = 0; i < sprites.Length; i++)
            sprites[i].color = restColor;
    }

    public override void QStart(QuantumGame game)
    {
        QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    private void OnDisable()
    {
        // QUpdate stops firing on a disabled/destroyed object (Unity calls OnDisable before
        // OnDestroy too), so a freeze in flight would strand Time.timeScale at 0 for whatever runs
        // next - same global-outlives-this-object hazard GameplayUiController force-restores against.
        if (_hitStopActive == false)
            return;

        _hitStopActive = false;
        Time.timeScale = _preHitStopTimeScale;
    }

    public override void QUpdate(QuantumGame game)
    {
        UpdateHitStop();
        UpdateDyingBlink(game);
    }

    private void UpdateHitStop()
    {
        if (_hitStopActive == false)
            return;

        if (Time.unscaledTime < _hitStopResumeAt)
            return;

        _hitStopActive = false;
        Time.timeScale = _preHitStopTimeScale;
    }

    private void UpdateDyingBlink(QuantumGame game)
    {
        if (MyLocalPlayer.Instance == null)
            return;

        Frame frame = game.Frames.Predicted;
        if (frame == null)
            return;

        bool anyDying = false;

        foreach (var slot in MyLocalPlayer.Instance.Slots)
        {
            if (slot.IsSet == false)
                continue;

            if (frame.TryGet<Health>(slot.EntityRef, out var health) == false || health.MaxHealth <= FP._0)
                continue;

            if (health.CurrentHealth <= FP._0)
                continue; // dead, not dying - respawn resets Health before this would ever see it

            float ratio = (health.CurrentHealth / health.MaxHealth).AsFloat;

            if (ratio <= dyingHealthPercent)
            {
                anyDying = true;
                break;
            }
        }

        if (anyDying == _isDying)
            return;

        _isDying = anyDying;

        for (var i = 0; i < sprites.Length; i++)
        {
            _tweens[i].Stop();
            _tweens[i] = _isDying
                ? Tween.Color(sprites[i], flashColor, dyingBlinkDuration, cycles: -1, cycleMode: CycleMode.Yoyo)
                : Tween.Color(sprites[i], restColor, duration);
        }
    }

    private void OnEntityDamaged(EventEntityDamaged e)
    {
        // Silent = a passive/self-inflicted tick (e.g. SentryDecaySystem) that shouldn't read as
        // "damage" - same rule HitFeedback/DamageFeedbackManager use to skip their own reactions.
        if (e.Silent == true)
            return;

        if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.IsLocalEntity(e.Target) == false)
            return;

        // Before the dying early-return below - the impact freeze should still land at low HP, even
        // when the sustained dying blink suppresses the one-shot Flash.
        TryTriggerHitStop(e);

        // Already looping the dying blink - a plain one-shot Flash would just cut that tween off
        // and race back to restColor, reading as a glitch instead of the sustained warning.
        if (_isDying == true)
            return;

        Flash();
    }

    // Scale the freeze to how hard the hit landed relative to THIS entity's own max HP, so a
    // glancing chip barely stutters while a big hit really slams. Reads MaxHealth off the predicted
    // frame (the same frame the rest of this widget already queries) - the event's Damage is the
    // final post-mitigation number the damage numbers show, i.e. actual HP/shield lost.
    private void TryTriggerHitStop(EventEntityDamaged e)
    {
        Frame frame = e.Game.Frames.Predicted;
        if (frame == null || frame.TryGet<Health>(e.Target, out var health) == false || health.MaxHealth <= FP._0)
            return;

        float damagePercent = (e.Damage / health.MaxHealth).AsFloat * 100f;
        float duration = ResolveHitStopDuration(damagePercent);
        TriggerHitStop(duration);
    }

    // Highest tier the hit reaches wins; order-independent so a designer can list rows however they
    // like. Below every tier's DamagePercent -> 0 (no freeze), same as an empty table.
    private float ResolveHitStopDuration(float damagePercent)
    {
        float duration = 0f;
        float bestPercent = -1f;

        foreach (var tier in hitStopTiers)
        {
            if (damagePercent >= tier.DamagePercent && tier.DamagePercent > bestPercent)
            {
                bestPercent = tier.DamagePercent;
                duration = tier.Duration;
            }
        }

        return duration;
    }

    private void TriggerHitStop(float duration)
    {
        if (duration <= 0f)
            return;

        if (_hitStopActive == false)
        {
            _preHitStopTimeScale = Time.timeScale;
            _hitStopActive = true;
        }

        Time.timeScale = 0f;
        // Max, not overwrite: a burst of same-frame hits (e.g. shotgun pellets) should settle on the
        // single longest freeze rather than the last one processed cutting a bigger one short.
        _hitStopResumeAt = Mathf.Max(_hitStopResumeAt, Time.unscaledTime + duration);
    }

    [Button]
    private void TestHitStop() => TriggerHitStop(ResolveHitStopDuration(100f));

    [Button]
    private void Flash()
    {
        for (var i = 0; i < sprites.Length; i++)
        {
            _tweens[i].Stop();
            sprites[i].color = flashColor;
            _tweens[i] = Tween.Color(sprites[i], flashColor, restColor, duration);
        }
    }
}
