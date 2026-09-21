using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

// Looping "living rift core" for UI: a center Image surrounded by a list of part Images, playing a slow,
// readable cycle on top of a constant, subtle idle:
//
//   1. PULSE  - the center swells and glows (optionally a weaker second beat, lub-dub).
//   2. PARTS  - shortly after, every part drifts away from the center and eases back, each one rolling its
//               OWN random start offset, spread, twist and speed from the shared ranges below.
//   3. PAUSE  - the pulse animation rests for a random beat, then the cycle repeats with freshly rolled
//               values, so no two cycles look the same.
//
// IDLE runs underneath all of it, never stopping: every part floats in a tiny slow loop with its own
// random phase and speed, and the center gently breathes. So even during the pause the core is never
// frozen - it just goes quiet.
//
// Each part's outward direction is captured once from its authored anchoredPosition relative to the
// center's (both must share a parent), so the layout you author in the Canvas is the resting pose the
// cycle and idle are both centered on - nothing ever drifts away from it. A part sitting exactly on the
// center has no defined direction and is fanned out by its array index instead.
//
// Each beat also brightens the Image: its color lerps toward white (glowAmount), then eases back to the
// authored color.
//
// Runs on unscaled time by default so it still plays while the game is time-paused.
public class CorePulseAnimation : MonoBehaviour
{
    [SerializeField] private Image center;
    [SerializeField, Tooltip("The pieces that shift apart and come back. Should share a parent with the center.")]
    private Image[] parts;

    [Header("1. Center pulse")]
    [SerializeField, Tooltip("Extra scale fraction the center swells by at the peak, e.g. 0.12 = +12%. 0 = center stays still.")]
    private float centerScaleAmount = 0.12f;
    [SerializeField, Tooltip("Seconds for the center to swell out.")]
    private float centerOutDuration = 0.3f;
    [SerializeField, Tooltip("Seconds for the center to settle back.")]
    private float centerBackDuration = 0.7f;
    [SerializeField, Tooltip("Adds a weaker second beat right after the first (lub-dub).")]
    private bool doubleBeat = true;
    [SerializeField, Tooltip("Seconds between the first beat ending and the second starting.")]
    private float doubleBeatGap = 0.1f;
    [SerializeField, Range(0f, 1f), Tooltip("Second beat strength as a fraction of the first.")]
    private float doubleBeatStrength = 0.6f;

    [Header("2. Parts (each rolls its own value from these ranges: x = min, y = max)")]
    [SerializeField, Tooltip("Seconds after the center pulse starts before the parts begin to move.")]
    private float partsLag = 0.3f;
    [SerializeField, Tooltip("Extra random wait per part on top of the lag, so the parts don't all leave together.")]
    private Vector2 partStagger = new Vector2(0f, 0.6f);
    [SerializeField, Tooltip("How far a part travels away from the center at the peak, in anchored-position units (pixels at 1:1 canvas scale).")]
    private Vector2 spreadDistance = new Vector2(15f, 40f);
    [SerializeField, Tooltip("Degrees a part twists at the peak (sign = direction, so a range spanning 0 twists both ways). 0/0 = no twist.")]
    private Vector2 twistDegrees = new Vector2(-6f, 6f);
    [SerializeField, Tooltip("Seconds for a part to drift out.")]
    private Vector2 partOutDuration = new Vector2(0.7f, 1.2f);
    [SerializeField, Tooltip("Seconds for a part to return to rest.")]
    private Vector2 partBackDuration = new Vector2(1.2f, 2f);

    [Header("3. Pause")]
    [SerializeField, Tooltip("Quiet time between the last part settling and the next center pulse (idle keeps running).")]
    private Vector2 pauseDuration = new Vector2(1.5f, 3.5f);

    [Header("Idle (always on, under the cycle)")]
    [SerializeField, Tooltip("How far each part floats around its rest position, in anchored-position units. 0 = no drift.")]
    private float idleDrift = 3f;
    [SerializeField, Tooltip("Degrees each part slowly sways. 0 = no sway.")]
    private float idleSway = 1.5f;
    [SerializeField, Tooltip("Idle speed in radians per second - each part rolls its own within this range.")]
    private Vector2 idleSpeed = new Vector2(0.5f, 1.2f);
    [SerializeField, Tooltip("Scale fraction the center breathes by, e.g. 0.02 = +/-2%. 0 = center is still at idle.")]
    private float idleBreath = 0.02f;
    [SerializeField, Tooltip("Center breathing speed in radians per second.")]
    private float idleBreathSpeed = 1.1f;

    [Header("Feel")]
    [SerializeField] private Ease outEase = Ease.OutSine;
    [SerializeField] private Ease backEase = Ease.InOutSine;
    [SerializeField, Range(0f, 1f), Tooltip("How far each Image's color lerps toward white at the peak of its beat (0 = no glow, 1 = fully white). Needs the Image's authored color to be darker than white to be visible.")]
    private float glowAmount = 0.4f;

    [Header("General")]
    [SerializeField] private bool playOnEnable = true;
    [SerializeField, Tooltip("Play on unscaled time so it still animates while the game is time-paused.")]
    private bool useUnscaledTime = true;

    // Authored baseline, captured once.
    private RectTransform[] _partRects;
    private Vector2[] _restPositions;
    private Quaternion[] _restRotations;
    private Vector2[] _directions;
    private Color[] _partBaseColors;
    private RectTransform _centerRect;
    private Vector3 _centerBaseScale;
    private Color _centerBaseColor;
    private bool _baselineCaptured;

    // Current cycle state, written by the tweens and composed with idle every frame in Update.
    private float[] _partSpread;   // pixels away from center along the part's direction
    private float[] _partTwist;    // degrees
    private float[] _partGlow;     // 0..1 (before glowAmount)
    private float _centerPulse;    // 0..1 (before centerScaleAmount / glowAmount)

    // Per-part idle phase/speed, rolled once per Play.
    private float[] _idlePhase;
    private float[] _idleRate;
    private float _centerIdlePhase;

    private bool _running;

    // Running animation of the current cycle: index 0 = center, index i + 1 = parts[i]; plus the timer
    // that starts the next cycle once the current one and its pause are over.
    private Sequence[] _chains;
    private Tween _nextCycle;

    private void Reset()
    {
        // Prefill from children: first Image found is a guess for the center, the rest are parts.
        Image[] all = GetComponentsInChildren<Image>(true);

        if (all.Length == 0)
            return;

        center = all[0];
        parts = new Image[all.Length - 1];
        Array.Copy(all, 1, parts, 0, parts.Length);
    }

    private void Awake()
    {
        EnsureBaselineCaptured();
    }

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    private void OnDisable()
    {
        Stop();
    }

    private void Update()
    {
        if (_running)
            Compose();
    }

    // Starts (or restarts) the idle and the cycle from the rest pose, beginning with a center pulse.
    public void Play()
    {
        EnsureBaselineCaptured();
        Stop();

        for (int i = 0; i < _partRects.Length; i++)
        {
            _idlePhase[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _idleRate[i] = RollRange(idleSpeed);
        }

        _centerIdlePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        _running = true;
        RunCycle();
    }

    // Stops idle and cycle and snaps all parts and the center back to their authored pose.
    public void Stop()
    {
        _running = false;
        _nextCycle.Stop();

        if (_chains != null)
        {
            for (int i = 0; i < _chains.Length; i++)
                _chains[i].Stop();
        }

        if (_baselineCaptured)
        {
            Array.Clear(_partSpread, 0, _partSpread.Length);
            Array.Clear(_partTwist, 0, _partTwist.Length);
            Array.Clear(_partGlow, 0, _partGlow.Length);
            _centerPulse = 0f;
            ResetPose();
        }
    }

    // One full cycle: center pulse, then every part on its own rolled timing, then a pause. The next
    // cycle is scheduled for when the longest animation ends plus a rolled pause.
    private void RunCycle()
    {
        float longest = 0f;

        if (_centerRect != null)
            longest = BuildCenterPulse();

        for (int i = 0; i < _partRects.Length; i++)
        {
            if (_partRects[i] != null)
                longest = Mathf.Max(longest, BuildPartMove(i));
        }

        float wait = longest + RollRange(pauseDuration);
        _nextCycle = Tween.Delay(wait, RunCycle, useUnscaledTime);
    }

    // Center swell (and optional weaker second beat). Returns the total duration.
    private float BuildCenterPulse()
    {
        Sequence seq = Sequence.Create(useUnscaledTime: useUnscaledTime);
        AddBeat(seq, 1f, centerOutDuration, centerBackDuration, t => _centerPulse = t);
        float total = centerOutDuration + centerBackDuration;

        if (doubleBeat)
        {
            float gap = Mathf.Max(doubleBeatGap, 0.01f);
            seq.ChainDelay(gap);
            AddBeat(seq, doubleBeatStrength, centerOutDuration, centerBackDuration, t => _centerPulse = t);
            total += gap + centerOutDuration + centerBackDuration;
        }

        _chains[0] = seq;
        return total;
    }

    // One part's drift: rolled start offset, then out, then back. Returns the total duration.
    private float BuildPartMove(int i)
    {
        float delay = partsLag + RollRange(partStagger);
        float spread = RollRange(spreadDistance);
        float twist = RollRange(twistDegrees);
        float outTime = RollRange(partOutDuration);
        float backTime = RollRange(partBackDuration);

        Sequence seq = Sequence.Create(useUnscaledTime: useUnscaledTime);

        if (delay > 0f)
            seq.ChainDelay(delay);

        AddBeat(seq, 1f, outTime, backTime, t =>
        {
            _partSpread[i] = spread * t;
            _partTwist[i] = twist * t;
            _partGlow[i] = t;
        });

        _chains[i + 1] = seq;
        return delay + outTime + backTime;
    }

    // Appends one out-and-back (0 -> strength -> 0) to a sequence.
    private void AddBeat(Sequence seq, float strength, float outTime, float backTime, Action<float> apply)
    {
        seq.Chain(Tween.Custom(0f, 1f, outTime, onValueChange: t => apply(t * strength), ease: outEase, useUnscaledTime: useUnscaledTime))
           .Chain(Tween.Custom(1f, 0f, backTime, onValueChange: t => apply(t * strength), ease: backEase, useUnscaledTime: useUnscaledTime));
    }

    // Combines the cycle state with the always-on idle and writes the final pose. Rest position is
    // the center of both, so neither can accumulate drift.
    private void Compose()
    {
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;

        for (int i = 0; i < _partRects.Length; i++)
        {
            if (_partRects[i] == null)
                continue;

            float a = now * _idleRate[i] + _idlePhase[i];
            Vector2 drift = new Vector2(Mathf.Sin(a), Mathf.Cos(a * 0.8f + 1.3f)) * idleDrift;
            float sway = Mathf.Sin(a * 0.7f + 0.5f) * idleSway;

            _partRects[i].anchoredPosition = _restPositions[i] + _directions[i] * _partSpread[i] + drift;
            _partRects[i].localRotation = _restRotations[i] * Quaternion.Euler(0f, 0f, _partTwist[i] + sway);
            parts[i].color = Glow(_partBaseColors[i], _partGlow[i]);
        }

        if (_centerRect != null)
        {
            float breath = Mathf.Sin(now * idleBreathSpeed + _centerIdlePhase) * idleBreath;
            _centerRect.localScale = _centerBaseScale * (1f + centerScaleAmount * _centerPulse + breath);
            center.color = Glow(_centerBaseColor, _centerPulse);
        }
    }

    // Lerps the RGB toward white by glowAmount * t, keeping the authored alpha untouched.
    private Color Glow(Color baseColor, float t)
    {
        Color c = Color.Lerp(baseColor, Color.white, glowAmount * t);
        c.a = baseColor.a;
        return c;
    }

    private void ResetPose()
    {
        for (int i = 0; i < _partRects.Length; i++)
        {
            if (_partRects[i] == null)
                continue;

            _partRects[i].anchoredPosition = _restPositions[i];
            _partRects[i].localRotation = _restRotations[i];
            parts[i].color = _partBaseColors[i];
        }

        if (_centerRect != null)
        {
            _centerRect.localScale = _centerBaseScale;
            center.color = _centerBaseColor;
        }
    }

    private static float RollRange(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);

    // Captured once, before anything ever moves, so Awake's values are the real authored ones and
    // never a mid-animation pose.
    private void EnsureBaselineCaptured()
    {
        if (_baselineCaptured)
            return;

        parts = parts ?? new Image[0];
        int count = parts.Length;
        _partRects = new RectTransform[count];
        _restPositions = new Vector2[count];
        _restRotations = new Quaternion[count];
        _directions = new Vector2[count];
        _partBaseColors = new Color[count];
        _partSpread = new float[count];
        _partTwist = new float[count];
        _partGlow = new float[count];
        _idlePhase = new float[count];
        _idleRate = new float[count];
        _chains = new Sequence[count + 1];

        _centerRect = center != null ? center.rectTransform : null;
        Vector2 centerPos = _centerRect != null ? _centerRect.anchoredPosition : Vector2.zero;

        for (int i = 0; i < count; i++)
        {
            if (parts[i] == null)
                continue;

            RectTransform rect = parts[i].rectTransform;
            Vector2 rest = rect.anchoredPosition;
            Vector2 offset = rest - centerPos;

            if (offset.sqrMagnitude < 0.0001f)
            {
                // No authored offset to push along - fan the piece out evenly by index instead.
                float angle = (i / (float)count) * Mathf.PI * 2f;
                offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            _partRects[i] = rect;
            _restPositions[i] = rest;
            _restRotations[i] = rect.localRotation;
            _directions[i] = offset.normalized;
            _partBaseColors[i] = parts[i].color;
        }

        if (_centerRect != null)
        {
            _centerBaseScale = _centerRect.localScale;
            _centerBaseColor = center.color;
        }

        _baselineCaptured = true;
    }
}
