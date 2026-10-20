using Coffee.UIExtensions;
using PrimeTween;
using Quantum;
using UnityEngine;
using UnityEngine.UI;

// One-shot "your skill is back" celebration on a skill button: a UIParticle shard burst, an
// expanding ring, a quick glow flash and an icon punch. Makes the end of a cooldown readable at a
// glance - the fill simply vanishing was too easy to miss mid-fight.
//
// Driven by SkillSystem's EventSkillReady (fired when a slot recovers from 0 stacks to 1 - the exact
// moment it goes from unusable to usable; later 1 -> 2 recoveries on a multi-charge slot don't fire,
// the skill was already usable). Filtered to the entity/slot of the SkillCooldownUiWidget it sits
// under, so the same prefab works on the CF2 touch buttons, the player HUD cluster and the party HUD.
public class SkillReadyBurstWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Which entity/slot to celebrate - defaults to the nearest SkillCooldownUiWidget in the parents.")]
    private SkillCooldownUiWidget skillWidget;

    [Header("Particles")]
    [SerializeField, Tooltip("Played (restarted) on every burst - normally the ParticleSystems under a UIParticle child.")]
    private ParticleSystem[] particles;
    [SerializeField, Tooltip("Optional - rescaled to this widget's own rect width so one prefab reads the same on a 45px party icon and a 250px touch button.")]
    private UIParticle uiParticle;
    [SerializeField, Tooltip("UIParticle.scale per 100px of this widget's width.")]
    private float particleScalePer100Px = 7.5f;

    [Header("Ring")]
    [SerializeField] private Image ringImage;
    [SerializeField] private float ringStartScale = 0.8f;
    [SerializeField] private float ringEndScale = 1.9f;
    [SerializeField] private float ringDuration = 0.45f;

    [Header("Flash")]
    [SerializeField] private Image flashImage;
    [SerializeField, Range(0f, 1f)] private float flashAlpha = 0.85f;
    [SerializeField] private float flashDuration = 0.35f;

    [Header("Icon punch")]
    [SerializeField, Tooltip("Optional - normally the skill icon.")]
    private Transform punchTarget;
    [SerializeField] private Vector3 punchStrength = new Vector3(0.3f, 0.3f, 0f);
    [SerializeField] private float punchDuration = 0.4f;
    [SerializeField] private float punchFrequency = 10f;

    [SerializeField, Tooltip("Real-time seconds - a prediction/rollback replay of the same SkillReady can't double-fire within this window.")]
    private float minInterval = 0.4f;

    private Vector3 _punchRestScale = Vector3.one;
    private float _lastBurstTime = float.NegativeInfinity;
    private Sequence _sequence;
    private Tween _punchTween;

    private void Awake()
    {
        if (skillWidget == null)
            skillWidget = GetComponentInParent<SkillCooldownUiWidget>(true);

        if (punchTarget != null)
            _punchRestScale = punchTarget.localScale;

        SetGraphicShown(ringImage, false);
        SetGraphicShown(flashImage, false);
        RefreshParticleScale();

        QuantumEvent.Subscribe<EventSkillReady>(this, OnSkillReady, onlyIfActiveAndEnabled: true);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    private void OnDisable()
    {
        _sequence.Stop();
        _punchTween.Stop();
        SetGraphicShown(ringImage, false);
        SetGraphicShown(flashImage, false);

        if (punchTarget != null)
            punchTarget.localScale = _punchRestScale;
    }

    private void OnRectTransformDimensionsChange()
    {
        RefreshParticleScale();
    }

    private void RefreshParticleScale()
    {
        if (uiParticle != null && transform is RectTransform rect && rect.rect.width > 0f)
            uiParticle.scale = particleScalePer100Px * rect.rect.width / 100f;
    }

    private void OnSkillReady(EventSkillReady e)
    {
        if (skillWidget == null || e.Entity != skillWidget.EntityRef || e.Slot != skillWidget.Slot)
            return;

        Play();
    }

    // Public so it can be previewed from the Inspector/editor tooling without a running match.
    [NaughtyAttributes.Button]
    public void Play()
    {
        if (Time.unscaledTime - _lastBurstTime < minInterval)
            return;

        _lastBurstTime = Time.unscaledTime;

        if (particles != null)
        {
            foreach (ParticleSystem system in particles)
            {
                if (system == null)
                    continue;

                system.Clear(true);
                system.Play(true);
            }
        }

        _sequence.Stop();
        _sequence = Sequence.Create(useUnscaledTime: true);

        if (ringImage != null)
        {
            SetGraphicShown(ringImage, true);
            _sequence.Group(Tween.Scale(ringImage.transform, Vector3.one * ringStartScale, Vector3.one * ringEndScale, ringDuration, Ease.OutCubic, useUnscaledTime: true));
            _sequence.Group(Tween.Alpha(ringImage, 1f, 0f, ringDuration, Ease.InQuad, useUnscaledTime: true));
        }

        if (flashImage != null)
        {
            SetGraphicShown(flashImage, true);
            _sequence.Group(Tween.Alpha(flashImage, flashAlpha, 0f, flashDuration, Ease.OutQuad, useUnscaledTime: true));
        }

        _sequence.OnComplete(this, target =>
        {
            SetGraphicShown(target.ringImage, false);
            SetGraphicShown(target.flashImage, false);
        });

        if (punchTarget != null)
        {
            // Reset-before-punch, same idiom as CurrencyUiWidget.PlayPunch - PunchScale punches
            // relative to the CURRENT scale, so an unfinished punch would otherwise compound.
            _punchTween.Stop();
            punchTarget.localScale = _punchRestScale;
            _punchTween = Tween.PunchScale(punchTarget, punchStrength, punchDuration, punchFrequency, useUnscaledTime: true);
        }
    }

    private static void SetGraphicShown(Graphic graphic, bool shown)
    {
        if (graphic != null && graphic.enabled != shown)
            graphic.enabled = shown;
    }
}
