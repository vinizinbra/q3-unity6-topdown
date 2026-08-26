using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEngine;

// Generic pooled toast popup - shared by Menu (PartyManager/MainMenuWindow) AND in-game HUD
// (InteractionPromptWidget/TraversalChallengeWidget) call sites, hence living in UI/Common rather
// than UI/Menu. Each scene that wants toasts owns its own ToastManager plus its own pooled
// ToastWidget children, so the menu and the gameplay HUD each pop toasts on their own Canvas.
//
// Both are alive at the same time while a match runs (QuantumGameScene loads additively on top of
// MenuScene), which is exactly why a single static field pointing at "the" manager cannot work:
// whichever Awake ran last claimed the field, and once THAT manager's scene unloaded the field was
// left holding a destroyed manager. Its C# object outlives the native one, so
// `ToastManager.Instance?.Show(...)` sailed straight past the null-conditional - `?.` is a plain
// reference check and does NOT use Unity's destroyed-object `==` - and then threw a
// MissingReferenceException on the first pooled widget it touched (all of which died with that
// same scene).
//
// So instead of one field there's a registry of every live manager, and Instance resolves to the
// most recently registered one that is still alive: the gameplay HUD while a match is up, and the
// menu again the instant that scene unloads. Neither manager ever touches the other's pool.
public class ToastManager : MonoBehaviour
{
    private const string LogTag = "Toast";

    // Registration order is scene load order, so the last live entry is the foreground manager.
    private static readonly List<ToastManager> Managers = new List<ToastManager>();
    private static string _pendingAfterSceneLoad;

    private ToastWidget[] _pool;

    // Always a REAL null when there is no usable manager - never a destroyed one - so every
    // existing `ToastManager.Instance?.Show(...)` call site stays correct exactly as written.
    public static ToastManager Instance
    {
        get
        {
            for (int i = Managers.Count - 1; i >= 0; i--)
            {
                // Unity's own == , which reports a manager destroyed with its scene as null.
                if (Managers[i] != null)
                    return Managers[i];

                Managers.RemoveAt(i);
            }

            return null;
        }
    }

    // Statics survive a Play Mode exit when Enter Play Mode Options disables domain reload - same
    // reset AudioManager/LocalPlayerAudioListener do, and for the same reason: a stale entry here
    // would be a destroyed manager from the previous session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Managers.Clear();
        _pendingAfterSceneLoad = null;
    }

    private void Awake()
    {
        Managers.Remove(this);
        Managers.Add(this);

        _pool = GetComponentsInChildren<ToastWidget>(true);
        foreach (var widget in _pool)
            widget.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Managers.Remove(this);
    }

    private void Start()
    {
        if (!string.IsNullOrEmpty(_pendingAfterSceneLoad))
        {
            Show(_pendingAfterSceneLoad);
            _pendingAfterSceneLoad = null;
        }
    }

    public void Show(string message)
    {
        var widget = ResolveFreeWidget();
        if (widget == null)
        {
            LogHelper.Warn(LogTag, $"Pool exhausted, dropping: {message}");
            return;
        }

        widget.Show(message);
    }

    // Deliberately not LINQ: this runs off gameplay events, and the `widget != null` test has to be
    // Unity's == overload anyway - a pool entry can be a widget already destroyed with its scene
    // while this manager itself is mid-teardown.
    private ToastWidget ResolveFreeWidget()
    {
        if (_pool == null)
            return null;

        foreach (var widget in _pool)
            if (widget != null && widget.CanUse)
                return widget;

        return null;
    }

    // Queues a toast that shows after the next scene load - use when a scene change is about to
    // happen and the current ToastManager (and its pool) will be destroyed with it. Consumed by
    // whichever manager reaches Start first.
    public static void ShowAfterSceneLoad(string message)
    {
        _pendingAfterSceneLoad = message;
    }
}
