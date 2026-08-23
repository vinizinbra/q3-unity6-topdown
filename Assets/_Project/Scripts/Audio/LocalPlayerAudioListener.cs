using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.SceneManagement;

// The game's ONE AudioListener, parked on the local player's spawned character instead of on the
// Main Camera.
//
// Why this exists: the gameplay camera sits high above the action on a fixed top-down offset (see
// FollowCamera.offset), so with the listener baked onto it - which is where Unity puts it by
// default, and where QuantumGameScene's Main Camera still has one authored - every 3D sound is
// heard from ~20 units up in the air. A shot fired at the player's feet and a shot fired across
// the room land at almost the same distance from that listener, so SoundData.minDistance /
// maxDistance / rolloff stop meaning anything useful and stereo panning collapses toward centre.
// Moving the listening point down onto the character makes those per-asset distances read in
// world units the designer actually thinks in.
//
// No scene authoring is required: if no instance was placed in a scene, one bootstraps itself
// after the first scene load, exactly like AudioManager's own lazy instance. It's persistent
// (DontDestroyOnLoad) rather than scene-local, so it survives QuantumGameScene loading/unloading
// around it and the game never ends up with zero listeners mid-transition - when there is no local
// player (menu, pre-spawn, post-match) it simply falls back to the active camera, i.e. the exact
// behaviour that existed before.
//
// Couch co-op: Unity allows exactly one AudioListener, so with two local players this listens from
// the midpoint between them - the same average FollowCamera already frames on.
public class LocalPlayerAudioListener : MonoBehaviour
{
    private const string LogTag = "Audio";

    public static LocalPlayerAudioListener Instance;

    [SerializeField, Tooltip("Off = behave exactly as before this component existed (listen from the camera). Here to A/B the difference without ripping the rig back out of the scene.")]
    private bool followLocalPlayer = true;

    [SerializeField, Tooltip("Raised off the character's feet, in world units, so a sound played at ground level isn't heard from literally inside its own emitter. Keep this small - the whole point is to listen from the action, not from the camera.")]
    private float heightOffset = 1f;

    [SerializeField, Min(0f), Tooltip("How fast the listening point eases toward its target, in units/sec of lerp speed. 0 = snap. The character transform this follows is already smoothed, so snapping is usually right; raise this only if a teleport (respawn, boss-arena teleport) is audible as a click.")]
    private float followSpeed;

    [SerializeField, Tooltip("Match the camera's rotation so stereo panning lines up with what's on screen (left on screen = left in the mix). Off = keep world-forward.")]
    private bool matchCameraRotation = true;

    // Every OTHER listener in the loaded scenes, disabled while this rig owns the audio. Kept so
    // they can be handed their job back if this rig is ever destroyed (e.g. a domain reload in the
    // Editor), rather than leaving the project with no listener at all.
    private readonly List<AudioListener> _suppressed = new List<AudioListener>();
    private AudioListener _listener;
    private static bool _quitting;

    // Statics survive a Play Mode exit when Enter Play Mode Options disables domain reload - same
    // reset AudioManager does for the same reason.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        _quitting = false;
    }

    // Bootstraps a rig if no scene placed one. AfterSceneLoad so the first scene's own cameras (and
    // their authored listeners) already exist and get suppressed on the first pass.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        var existing = FindFirstObjectByType<LocalPlayerAudioListener>();
        if (existing != null)
            return;

        new GameObject(nameof(LocalPlayerAudioListener)).AddComponent<LocalPlayerAudioListener>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _listener = GetComponent<AudioListener>();
        if (_listener == null)
            _listener = gameObject.AddComponent<AudioListener>();
        _listener.enabled = true;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);

        SuppressOtherListeners();
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Instance = null;

        // Don't hand the job back on the way out of Play Mode - the objects are being torn down
        // anyway, and re-enabling a listener on a half-destroyed camera just logs noise.
        if (_quitting)
            return;

        foreach (var listener in _suppressed)
            if (listener != null)
                listener.enabled = true;

        _suppressed.Clear();
    }

    private void OnApplicationQuit() => _quitting = true;

    // A newly-loaded scene (QuantumGameScene, additively, every match) brings its own Main Camera
    // and its own authored listener with it - suppress it the moment it appears rather than
    // letting Unity pick a winner between the two.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SuppressOtherListeners();

    private void OnSceneUnloaded(Scene scene) => _suppressed.RemoveAll(l => l == null);

    private void SuppressOtherListeners()
    {
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (listener == _listener || listener.enabled == false)
                continue;

            listener.enabled = false;
            if (_suppressed.Contains(listener) == false)
                _suppressed.Add(listener);

            LogHelper.Log(LogTag, $"Disabled a second AudioListener on '{listener.gameObject.name}' - {nameof(LocalPlayerAudioListener)} owns the listening point now.", listener);
        }
    }

    // LateUpdate, so this reads the character's view transform after Quantum's entity views have
    // already been moved for this frame (and after FollowCamera's own Update-driven follow).
    private void LateUpdate()
    {
        if (_listener == null)
            return;

        var target = ResolveTargetPosition();
        transform.position = followSpeed > 0f
            ? Vector3.Lerp(transform.position, target, Time.unscaledDeltaTime * followSpeed)
            : target;

        if (matchCameraRotation == false)
            return;

        var camera = ResolveCamera();
        if (camera != null)
            transform.rotation = camera.rotation;
    }

    // Midpoint of every local player that's actually spawned; the camera itself when none is (menu,
    // between matches, or the handful of frames before the local character view registers).
    private Vector3 ResolveTargetPosition()
    {
        if (followLocalPlayer == true && MyLocalPlayer.Instance != null)
        {
            var sum = Vector3.zero;
            var count = 0;

            foreach (var slot in MyLocalPlayer.Instance.Slots)
            {
                if (slot.IsSet == false || slot.View == null || slot.View.viewTransform == null)
                    continue;

                sum += slot.View.viewTransform.position;
                count++;
            }

            if (count > 0)
                return sum / count + Vector3.up * heightOffset;
        }

        var camera = ResolveCamera();
        return camera != null ? camera.position : transform.position;
    }

    private Transform ResolveCamera()
    {
        if (FollowCamera.I != null)
            return FollowCamera.I.transform;

        return Camera.main != null ? Camera.main.transform : null;
    }

    [Button("Log Listening Point")]
    private void LogListeningPoint()
    {
        var localPlayers = 0;
        if (MyLocalPlayer.Instance != null)
            foreach (var slot in MyLocalPlayer.Instance.Slots)
                if (slot.IsSet == true && slot.View != null)
                    localPlayers++;

        LogHelper.Log(LogTag, $"Listening from {transform.position} - {localPlayers} local player(s) tracked, {_suppressed.Count} other listener(s) suppressed.", this);
    }
}
