using System.Collections.Generic;
using UnityEngine;

// Deliberately NOT a PgSingleton<T> - this lives on the Camera baked into QuantumGameScene, and
// PgSingleton.Awake()/.I both call DontDestroyOnLoad on the GameObject they're attached to. That
// would pull the gameplay Camera (+ its AudioListener) out of QuantumGameScene entirely, so it
// survives the scene's unload at match end - the next match's freshly-loaded QuantumGameScene then
// gets a second Camera+AudioListener, and this component's own duplicate-singleton guard only
// destroys itself (Destroy(this)), not the new scene's Camera GameObject, leaving both active at
// once. A plain scene-local static ties this camera's lifecycle to QuantumGameScene's own, exactly
// like the equivalent FollowCamera in the Jelly Upgrade project.
public class FollowCamera : MonoBehaviour
{
    public static FollowCamera I;

    public Vector3 offset;
    public float speed;

    [Header("Multi-target framing")]
    [Tooltip("Zoom multiplier applied to offset when all targets sit on top of each other.")]
    public float minZoom = 1f;
    [Tooltip("Zoom multiplier applied to offset once targets are spreadReference (or more) apart.")]
    public float maxZoom = 2.2f;
    [Tooltip("World-unit distance from the framed center that maps to maxZoom.")]
    public float spreadReference = 10f;
    [Tooltip("How fast the zoom itself eases toward its desired value, independent of position speed.")]
    public float zoomLerpSpeed = 5f;

    private readonly List<Transform> _targets = new List<Transform>();
    private float _zoom = 1f;
    private Vector3 _smoothedPosition;

    // Shake state - additive offset on top of the framed position, decaying linearly over
    // _shakeDuration. A later Shake() call only takes over if it's stronger than what's currently
    // playing, so a weak shot can't cut off a strong one still ringing out.
    private float _shakeElapsed;
    private float _shakeDuration;
    private float _shakeAmplitude;
    private float _shakeFrequency;
    private Vector2 _shakeSeed;

    private void Awake()
    {
        I = this;
        _shakeSeed = new Vector2(Random.value * 100f, Random.value * 100f);
        _smoothedPosition = transform.position;
    }

    private void OnDestroy()
    {
        if (I == this)
            I = null;
    }

    public void AddTarget(Transform target)
    {
        if (target != null && _targets.Contains(target) == false)
            _targets.Add(target);
    }

    public void RemoveTarget(Transform target)
    {
        _targets.Remove(target);
    }

    public void Shake(float amplitude, float duration, float frequency)
    {
        bool currentlyShaking = _shakeElapsed < _shakeDuration;
        if (currentlyShaking && _shakeAmplitude >= amplitude)
            return;

        _shakeElapsed = 0f;
        _shakeDuration = duration;
        _shakeAmplitude = amplitude;
        _shakeFrequency = frequency;
    }

    private void Update()
    {
        _targets.RemoveAll(t => t == null);
        if (_targets.Count == 0)
            return;

        Vector3 center = Vector3.zero;
        foreach (var t in _targets)
            center += t.position;
        center /= _targets.Count;

        float spread = 0f;
        foreach (var t in _targets)
            spread = Mathf.Max(spread, Vector3.Distance(t.position, center));

        float desiredZoom = Mathf.Clamp(1f + spread / spreadReference, minZoom, maxZoom);
        _zoom = Mathf.Lerp(_zoom, desiredZoom, Time.deltaTime * zoomLerpSpeed);

        Vector3 desiredPosition = center + offset * _zoom;
        _smoothedPosition = Vector3.Lerp(_smoothedPosition, desiredPosition, Time.deltaTime * speed);
        transform.position = _smoothedPosition + ResolveShakeOffset();
    }

    private Vector3 ResolveShakeOffset()
    {
        if (_shakeElapsed >= _shakeDuration)
            return Vector3.zero;

        _shakeElapsed += Time.deltaTime;
        float falloff = 1f - Mathf.Clamp01(_shakeElapsed / _shakeDuration);
        float t = Time.time * _shakeFrequency;

        float x = (Mathf.PerlinNoise(_shakeSeed.x, t) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(_shakeSeed.y, t) - 0.5f) * 2f;

        return new Vector3(x, y, 0f) * _shakeAmplitude * falloff;
    }
}
