using Coffee.UIExtensions;
using PrimeTween;
using Quantum;
using UnityEngine;
using UnityEngine.UI;

// One-shot "your skill is back" celebration on a skill button: a UIParticle shard burst, an
// expanding ring, a quick glow flash, light beams rising from behind the button (tinted per slot)
// and an icon punch. Makes the end of a cooldown readable at a
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

    [Header("Light beams")]
    [SerializeField, Tooltip("Vertical light shafts rising off the button (each a CanvasGroup holding a coloured glow + white core). Pivot at the bottom - they grow upward. Played in array order, one stagger apart.")]
    private CanvasGroup[] beams;
    [SerializeField, Tooltip("Holds the beams. At runtime it's moved out to be the sibling right behind the button this widget sits in (our parent), so the light rises from behind the button instead of over the icon.")]
    private RectTransform beamsRoot;
    [SerializeField] private float beamStagger = 0.035f;
    [SerializeField, Tooltip("Extra random 0..N seconds per beam so the fan never plays identically twice.")]
    private float beamStaggerJitter = 0.025f;
    [SerializeField, Tooltip("Random +/- fraction applied to each beam's height per play.")]
    private float beamHeightJitter = 0.2f;
    [SerializeField, Tooltip("Shoot up (OutBack overshoot) + snap from wide to thin.")]
    private float beamRiseDuration = 0.14f;
    [SerializeField, Tooltip("Width multiplier the beam starts at - snaps down to 1 during the rise.")]
    private float beamPopWidth = 1.8f;
    [SerializeField, Tooltip("Lift off + thin out + fade away after the rise.")]
    private float beamFadeDuration = 0.45f;
    [SerializeField, Tooltip("How far the beam flies upward while fading, as a fraction of its own height.")]
    private float beamLift = 0.45f;
    [SerializeField, Tooltip("Width multiplier the beam thins to while fading.")]
    private float beamEndWidth = 0.15f;

    [Header("Slot tint")]
    [SerializeField, Tooltip("Recoloured on every play (alpha kept) - normally the ring and the beams' glow layers. White cores/flash stay white so the effect keeps a hot centre. The particles' Start Color is retinted too (random between tint and white).")]
    private Graphic[] tintedGraphics;
    [SerializeField, ColorUsage(false)] private Color dashTint = new Color(0.25f, 0.9f, 1f);
    [SerializeField, ColorUsage(false), Tooltip("HeroSkill slot uses the hero's CharacterData.RingColor - this is only the fallback when it can't be resolved.")]
    private Color heroSkillFallbackTint = new Color(0.992f, 0.224f, 0.443f);

    [Header("Icon punch")]
    [SerializeField, Tooltip("Optional - normally the skill icon.")]
    private Transform punchTarget;
    [SerializeField] private Vector3 punchStrength = new Vector3(0.3f, 0.3f, 0f);
    [SerializeField] private float punchDuration = 0.4f;
    [SerializeField] private float punchFrequency = 10f;

    [SerializeField, Tooltip("Real-time seconds - a prediction/rollback replay of the same SkillReady can't double-fire within this window.")]
    private float minInterval = 0.4f;

    private Vector3 _punchRestScale = Vector3.one;
    private Vector2[] _beamRestPositions;
    private bool _beamsRootDetached;
    private float _lastBurstTime = float.NegativeInfinity;
    private Sequence _sequence;
    private Tween _punchTween;

    private void Awake()
    {
        if (skillWidget == null)
            skillWidget = GetComponentInParent<SkillCooldownUiWidget>(true);

        if (punchTarget != null)
            _punchRestScale = punchTarget.localScale;

        if (beams != null)
        {
            _beamRestPositions = new Vector2[beams.Length];
            for (int i = 0; i < beams.Length; i++)
            {
                if (beams[i] != null)
                    _beamRestPositions[i] = ((RectTransform)beams[i].transform).anchoredPosition;
            }
        }

        DetachBeamsRoot();

        SetGraphicShown(ringImage, false);
        SetGraphicShown(flashImage, false);
        HideBeams();
        RefreshParticleScale();

        QuantumEvent.Subscribe<EventSkillReady>(this, OnSkillReady, onlyIfActiveAndEnabled: true);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);

        // Lives outside our hierarchy once detached - nothing else would clean it up.
        if (_beamsRootDetached && beamsRoot != null)
            Destroy(beamsRoot.gameObject);
    }

    // Moves beamsRoot to be the sibling right before the button (our parent) - drawn before the
    // button's own graphic, so behind it. Ignores layout so a LayoutGroup on the button's parent
    // doesn't treat it as one more slot; its rect is re-matched to ours on every play.
    private void DetachBeamsRoot()
    {
        Transform button = transform.parent;
        if (beamsRoot == null || button == null || button.parent == null)
            return;

        beamsRoot.SetParent(button.parent, false);
        beamsRoot.SetSiblingIndex(button.GetSiblingIndex());

        LayoutElement layout = beamsRoot.GetComponent<LayoutElement>();
        if (layout == null)
            layout = beamsRoot.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        _beamsRootDetached = true;
        SyncBeamsRoot();
    }

    private void SyncBeamsRoot()
    {
        if (!_beamsRootDetached || beamsRoot == null)
            return;

        RectTransform self = (RectTransform)transform;
        Vector3 parentScale = beamsRoot.parent.lossyScale;
        Vector3 selfScale = self.lossyScale;

        beamsRoot.anchorMin = beamsRoot.anchorMax = new Vector2(0.5f, 0.5f);
        beamsRoot.pivot = self.pivot;
        beamsRoot.sizeDelta = self.rect.size;
        beamsRoot.localScale = new Vector3(selfScale.x / parentScale.x, selfScale.y / parentScale.y, 1f);
        beamsRoot.position = self.position;
    }

    private void OnDisable()
    {
        _sequence.Stop();
        _punchTween.Stop();
        SetGraphicShown(ringImage, false);
        SetGraphicShown(flashImage, false);
        HideBeams();

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

        // Before the particles restart so the burst is emitted with this slot's Start Color.
        ApplyTint(ResolveTint());
        SyncBeamsRoot();

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

        if (beams != null)
        {
            for (int i = 0; i < beams.Length; i++)
            {
                CanvasGroup beam = beams[i];
                if (beam == null)
                    continue;

                // Start state set up front - Insert'ed tweens don't touch their target until their
                // start time, so a staggered beam would otherwise sit at its last frame meanwhile.
                RectTransform beamRect = (RectTransform)beam.transform;
                float restY = _beamRestPositions[i].y;
                beamRect.anchoredPosition = _beamRestPositions[i];
                beamRect.localScale = new Vector3(beamPopWidth, 0f, 1f);
                beam.alpha = 0f;
                beam.gameObject.SetActive(true);

                float height = 1f + Random.Range(-beamHeightJitter, beamHeightJitter);
                float start = i * beamStagger + Random.Range(0f, beamStaggerJitter);
                float fadeStart = start + beamRiseDuration;
                _sequence.Insert(start, Tween.ScaleY(beamRect, 0f, height, beamRiseDuration, Ease.OutBack, useUnscaledTime: true));
                _sequence.Insert(start, Tween.ScaleX(beamRect, beamPopWidth, 1f, beamRiseDuration, Ease.OutCubic, useUnscaledTime: true));
                _sequence.Insert(start, Tween.Alpha(beam, 0f, 1f, beamRiseDuration * 0.5f, Ease.OutQuad, useUnscaledTime: true));
                _sequence.Insert(fadeStart, Tween.UIAnchoredPositionY(beamRect, restY, restY + beamRect.rect.height * height * beamLift, beamFadeDuration, Ease.InCubic, useUnscaledTime: true));
                _sequence.Insert(fadeStart, Tween.ScaleX(beamRect, 1f, beamEndWidth, beamFadeDuration, Ease.InQuad, useUnscaledTime: true));
                _sequence.Insert(fadeStart, Tween.Alpha(beam, 1f, 0f, beamFadeDuration, Ease.InQuad, useUnscaledTime: true));
            }
        }

        _sequence.OnComplete(this, target =>
        {
            SetGraphicShown(target.ringImage, false);
            SetGraphicShown(target.flashImage, false);
            target.HideBeams();
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

    // Dash reads as cool cyan; the hero skill wears the hero's own RingColor (same lookup as
    // PlayerNumberUiWidget) so each hero's signature move celebrates in their colour.
    private Color ResolveTint()
    {
        if (skillWidget == null || skillWidget.Slot == SkillSlotId.DashSkill)
            return dashTint;

        QuantumGame game = QuantumRunner.DefaultGame;
        if (game?.Frames.Predicted is Frame frame && frame.TryGet<CharacterStats>(skillWidget.EntityRef, out var stats))
        {
            CharacterData data = frame.FindAsset(stats.CharacterData);
            if (data != null)
                return data.RingColor;
        }

        return heroSkillFallbackTint;
    }

    private void ApplyTint(Color tint)
    {
        if (tintedGraphics != null)
        {
            foreach (Graphic graphic in tintedGraphics)
            {
                if (graphic == null)
                    continue;

                tint.a = graphic.color.a;
                graphic.color = tint;
            }
        }

        if (particles != null)
        {
            tint.a = 1f;
            foreach (ParticleSystem system in particles)
            {
                if (system == null)
                    continue;

                ParticleSystem.MainModule main = system.main;
                main.startColor = new ParticleSystem.MinMaxGradient(tint, Color.white);
            }
        }
    }

    private void HideBeams()
    {
        if (beams == null)
            return;

        for (int i = 0; i < beams.Length; i++)
        {
            CanvasGroup beam = beams[i];
            if (beam == null)
                continue;

            beam.gameObject.SetActive(false);
            beam.transform.localScale = Vector3.one;
            if (_beamRestPositions != null)
                ((RectTransform)beam.transform).anchoredPosition = _beamRestPositions[i];
        }
    }

    private static void SetGraphicShown(Graphic graphic, bool shown)
    {
        if (graphic != null && graphic.enabled != shown)
            graphic.enabled = shown;
    }
}
