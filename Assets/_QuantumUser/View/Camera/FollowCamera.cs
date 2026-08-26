using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.SceneManagement;

// Deliberately NOT a PgSingleton<T> - this lives on the Camera baked into QuantumGameScene, and
// PgSingleton.Awake()/.I both call DontDestroyOnLoad on the GameObject they're attached to. That
// would pull the gameplay Camera (+ its AudioListener) out of QuantumGameScene entirely, so it
// survives the scene's unload at match end - the next match's freshly-loaded QuantumGameScene then
// gets a second Camera+AudioListener, and this component's own duplicate-singleton guard only
// destroys itself (Destroy(this)), not the new scene's Camera GameObject, leaving both active at
// once. A plain scene-local static ties this camera's lifecycle to QuantumGameScene's own, exactly
// like the equivalent FollowCamera in the Jelly Upgrade project.
//
// That alone isn't enough, though: this component never calls DontDestroyOnLoad, but ANY other
// script can still drag the whole camera out of QuantumGameScene - by calling DontDestroyOnLoad on
// any component that sits on it (Camera/AudioListener/a shake listener), or by parenting it under
// an object that already lives in the DontDestroyOnLoad scene, which pulls the whole subtree along
// with it. A camera that escaped that way survives the match's own scene unload, so after a
// disconnect it keeps rendering (and keeps its AudioListener alive) over the menu, and the next
// match's freshly-loaded scene brings a second Camera+AudioListener up alongside it. EnforceHomeScene
// below detects that, names what's around the camera so the culprit can be identified, and puts it
// back where it belongs - or destroys it outright once its own scene is gone.
public class FollowCamera : MonoBehaviour
{
    private const string LogTag = "Camera";

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

    // While set, framing locks onto this single transform instead of averaging _targets - used for
    // the boss encounter's camera-focus cutaway (see BossWidget). _targets themselves are left
    // completely untouched (no AddTarget/RemoveTarget churn), so clearing this instantly resumes
    // normal multi-player framing exactly where it would have been anyway.
    private Transform _focusOverrideTarget;

    // The scene this camera was loaded with (QuantumGameScene). Captured in Awake so the guard can
    // tell "still where I belong" apart from "someone moved me into DontDestroyOnLoad".
    private Scene _homeScene;
    private bool _reportedEscape;

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

        _homeScene = gameObject.scene;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;

        if (I == this)
            I = null;
    }

    // Only ever reached by a camera that was NOT destroyed along with its own scene - i.e. one
    // sitting in DontDestroyOnLoad that EnforceHomeScene couldn't put back (its scene was already
    // on the way out when it noticed). Nothing should outlive the match here, so it goes.
    private void OnSceneUnloaded(Scene scene)
    {
        if (scene != _homeScene || this == null)
            return;

        LogHelper.Warn(LogTag,
            $"'{name}' outlived '{scene.name}' (now in '{gameObject.scene.name}') - destroying it so it can't render over the menu or collide with the next match's own camera.",
            this);

        Destroy(gameObject);
    }

    // See the class comment. Cheap enough to poll (a scene-handle compare), and polling is the only
    // option - Unity raises no callback for "this object changed scene".
    private bool EnforceHomeScene()
    {
        if (gameObject.scene == _homeScene)
            return true;

        if (_reportedEscape == false)
        {
            _reportedEscape = true;
            LogHelper.Error(LogTag,
                $"'{name}' was moved out of '{_homeScene.name}' into '{gameObject.scene.name}' by something other than {nameof(FollowCamera)}" +
                $" - parent='{(transform.parent != null ? transform.parent.name : "<none>")}', root='{transform.root.name}', children=[{DescribeChildren()}]." +
                " Whatever owns those objects is what to fix; putting the camera back for now.",
                this);
        }

        // A camera dragged along by a DontDestroyOnLoad parent has to be detached first - both
        // because MoveGameObjectToScene only accepts root objects, and because staying parented
        // would just pull it straight back out again.
        if (transform.parent != null)
            transform.SetParent(null, worldPositionStays: true);

        if (_homeScene.IsValid() && _homeScene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(gameObject, _homeScene);
            return true;
        }

        // Its scene is already gone, so there is nothing left for this camera to follow.
        Destroy(gameObject);
        return false;
    }

    private string DescribeChildren()
    {
        if (transform.childCount == 0)
            return "<none>";

        var names = new string[transform.childCount];
        for (int i = 0; i < transform.childCount; i++)
            names[i] = transform.GetChild(i).name;

        return string.Join(", ", names);
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

    // Locks framing onto a single target (e.g. the boss) instead of the normal multi-player
    // average - snap defaults true since this is meant to be called while the screen is hidden
    // behind a fade (see ScreenFadeWidget/BossWidget), so the camera should already be exactly on
    // target the instant the fade reveals it, not still easing toward it.
    public void SetFocusOverride(Transform target, bool snap = true)
    {
        _focusOverrideTarget = target;

        if (snap == true && target != null)
            _smoothedPosition = target.position + offset * _zoom;
    }

    // Resumes normal multi-player framing - same snap-by-default reasoning as SetFocusOverride
    // above, so the return cut is also hidden cleanly behind a fade rather than panning back.
    public void ClearFocusOverride(bool snap = true)
    {
        _focusOverrideTarget = null;

        if (snap == false || _targets.Count == 0)
            return;

        Vector3 center = Vector3.zero;
        foreach (var t in _targets)
            center += t.position;
        center /= _targets.Count;

        _smoothedPosition = center + offset * _zoom;
    }

    private void Update()
    {
        if (EnforceHomeScene() == false)
            return;

        Vector3 center;
        float spread;

        if (_focusOverrideTarget != null)
        {
            center = _focusOverrideTarget.position;
            spread = 0f;
        }
        else
        {
            _targets.RemoveAll(t => t == null);
            if (_targets.Count == 0)
                return;

            center = Vector3.zero;
            foreach (var t in _targets)
                center += t.position;
            center /= _targets.Count;

            spread = 0f;
            foreach (var t in _targets)
                spread = Mathf.Max(spread, Vector3.Distance(t.position, center));
        }

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
