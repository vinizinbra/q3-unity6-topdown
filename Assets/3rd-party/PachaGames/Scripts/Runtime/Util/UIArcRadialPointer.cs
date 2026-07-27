using System.Collections.Generic;
using CodeStage.AntiCheat.ObscuredTypes;
using TMPro;
using UnityEngine;
#if NAUGHTY_ATTRIBUTES
using NaughtyAttributes; // optional for [Button]
#endif

[System.Serializable]
public class MultiplierValues
{
    public ObscuredFloat to;
    public ObscuredFloat multipliedValue;
}
[ExecuteAlways]
public class UIArcRadialPointer : MonoBehaviour
{
    public List<MultiplierValues> multiplierValuesList = new List<MultiplierValues>();
    public TMP_Text[] texts;

    public void CheckMultiplier()
    {
        bool found = false;
        for (int i = 0; i < multiplierValuesList.Count; i++)
        {
            if (!found && CurrentAngleFromTop > multiplierValuesList[i].to)
            {
                found = true;
                texts[i].transform.localScale = Vector3.one * 1.2f;
            }
            else
            {
                texts[i].transform.localScale = Vector3.one;
            }

        }
    }
    public float GetCurrentMultiplierValue()
    {

        foreach (var mv in multiplierValuesList)
        {
            if(CurrentAngleFromTop > mv.to)
                return mv.multipliedValue;
        }
        return multiplierValuesList[0].multipliedValue;    
    }
    [Header("References")]
    public RectTransform arrow; // sprite must point UP at 0°, pivot centered

    [Header("Arc (degrees, relative to UP)")]
    [Range(-180,180)] public float startAngleFromTop = -60f;
    [Range(-180,180)] public float endAngleFromTop   =  60f;
    public float radius = 120f;

    [Header("Motion")]
    [Tooltip("Time to go start -> end (full back-and-forth = 2x).")]
    public float duration = 1.0f;
    public bool unscaledTime = true;
    public bool autoPlay = true;

    [Header("Offsets")]
    [Tooltip("Rotates the entire arc (affects position + reported angle).")]
    public float arcAngleOffsetFromTop = 0f;
    [Tooltip("Extra twist after aiming radial-out, for sprite alignment.")]
    public float rotationOffsetDeg = 0f;
    [Tooltip("Push/pull along radius (px).")]
    public float radialOffset = 0f;
    [Tooltip("Slide along tangent (px).")]
    public float tangentialOffset = 0f;

    [Header("Local Position Offset (UI parent space)")]
    public Vector2 localOffset = Vector2.zero;
    [Tooltip("If ON, rotation uses the offset position; otherwise it ignores it.")]
    public bool offsetAffectsRotation = false;

    [Header("Inspector Scrub")]
    public bool manualControl = false;
    [Range(0f,1f)] public float inspectorNormalized = 0f; // 0 = start, 1 = end

    [Header("Optional rotation damping")]
    [Tooltip("Max deg/sec for rotation smoothing (0 = snap).")]
    public float maxRotateSpeedDegPerSec = 0f;

    // --- Runtime state ---
    [SerializeField, Tooltip("Readonly: angle (deg) relative to UP, includes arcAngleOffsetFromTop.")]
    private float _currentAngleFromTop;
    public float CurrentAngleFromTop => _currentAngleFromTop;       // e.g., -60..+60 (+ arc offset)
    public Vector2 CurrentRadialDir { get; private set; }           // normalized (center -> arc point)
    public float CurrentStandardAngleDeg { get; private set; }      // 0°=right, CCW+

    private bool _playing;
    private float _phase; // drives sin() for smooth ping-pong
    private Quaternion _lastRot;

    void Reset()
    {
        if (!arrow && transform.childCount > 0)
            arrow = transform.GetChild(0) as RectTransform;
    }

    void OnEnable()
    {
        if (autoPlay) Play();
        _phase = 0f;
        if (arrow)
        {
            _lastRot = arrow.localRotation;
            ApplyAtAngle(CalcAngleFromPhase(_phase));
        }
    }

    void Update()
    {
        if (!arrow) return;

        if (manualControl)
        {
            _phase = PhaseFromNormalized(inspectorNormalized);
        }
        else if (_playing && duration > 0f)
        {
            CheckMultiplier();
            float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float omega = Mathf.PI / Mathf.Max(0.0001f, duration); // start->end in 'duration'
            _phase += dt * omega;
        }

        _currentAngleFromTop = CalcAngleFromPhase(_phase);
        ApplyAtAngle(_currentAngleFromTop);
    }

    void OnValidate()
    {
        if (!arrow) return;
        if (manualControl) _phase = PhaseFromNormalized(inspectorNormalized);
        _currentAngleFromTop = CalcAngleFromPhase(_phase);
        ApplyAtAngle(_currentAngleFromTop);
    }

    // ---------- Math helpers ----------
    float PhaseFromNormalized(float t01)
    {
        return Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, Mathf.Clamp01(t01)); // 0..1 → -π/2..+π/2
    }

    float CalcAngleFromPhase(float phase)
    {
        float mid = 0.5f * (startAngleFromTop + endAngleFromTop);
        float halfSpan = 0.5f * (endAngleFromTop - startAngleFromTop);
        return mid + halfSpan * Mathf.Sin(phase) + arcAngleOffsetFromTop;
    }

    void ApplyAtAngle(float angleFromTopDeg)
    {
        // Convert “from top” to standard angle (0° = +X/right)
        float standardDeg = 90f + angleFromTopDeg;
        float phi = standardDeg * Mathf.Deg2Rad;

        // Basis at the arc point
        Vector2 radial = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
        Vector2 tangent = new Vector2(-Mathf.Sin(phi), Mathf.Cos(phi));

        // Base (arc) position with arc-space offsets
        float r = radius + radialOffset;
        Vector2 basePos = radial * r + tangentialOffset * tangent;

        // Final position with local UI offset (parent space)
        Vector2 finalPos = basePos + localOffset;
        arrow.anchoredPosition = finalPos;

        // Rotation: default uses basePos (pure radial from center), optionally use finalPos
        Vector2 rotPos = offsetAffectsRotation ? finalPos : basePos;
        float aim = Mathf.Atan2(rotPos.y, rotPos.x) * Mathf.Rad2Deg;   // 0°=right, CCW+
        float zDeg = (aim - 90f) + rotationOffsetDeg;                  // sprite UP → radial-out

        CurrentStandardAngleDeg = aim;
        CurrentRadialDir = basePos.sqrMagnitude > 0f ? basePos.normalized : Vector2.up;

        Quaternion target = Quaternion.Euler(0f, 0f, zDeg);
        if (maxRotateSpeedDegPerSec > 0f && Application.isPlaying)
        {
            float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _lastRot = Quaternion.RotateTowards(_lastRot, target, maxRotateSpeedDegPerSec * dt);
            arrow.localRotation = _lastRot;
        }
        else
        {
            _lastRot = target;
            arrow.localRotation = target;
        }
    }

    // ---------- Controls ----------
    #if NAUGHTY_ATTRIBUTES
    [Button("Play")]
    #endif
    [ContextMenu("Play")]
    public void Play()  { _playing = true; }

    #if NAUGHTY_ATTRIBUTES
    [Button("Stop")]
    #endif
    [ContextMenu("Stop")]
    public void Stop()  { _playing = false; }

    #if NAUGHTY_ATTRIBUTES
    [Button("Restart")]
    #endif
    [ContextMenu("Restart")]
    public void Restart()
    {
        _phase = 0f;
        _playing = true;
    }

    public void SetNormalized(float t01)
    {
        manualControl = true;
        inspectorNormalized = Mathf.Clamp01(t01);
        _phase = PhaseFromNormalized(inspectorNormalized);
        _currentAngleFromTop = CalcAngleFromPhase(_phase);
        ApplyAtAngle(_currentAngleFromTop);
    }
}
