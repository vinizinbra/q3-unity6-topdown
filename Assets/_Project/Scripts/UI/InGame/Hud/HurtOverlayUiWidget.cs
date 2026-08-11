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

    private Tween[] _tweens;
    private bool _isDying;

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

    public override void QUpdate(QuantumGame game)
    {
        UpdateDyingBlink(game);
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

        // Already looping the dying blink - a plain one-shot Flash would just cut that tween off
        // and race back to restColor, reading as a glitch instead of the sustained warning.
        if (_isDying == true)
            return;

        Flash();
    }

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
