using System.Linq;
using QuantumUser.View.Util;
using UnityEngine;

// Generic pooled toast popup - shared by Menu (PartyManager/MainMenuWindow) AND in-game HUD
// (InteractionPromptWidget) call sites, hence living in UI/Common rather than UI/Menu. Each scene
// that wants toasts needs its own ToastManager + pooled ToastWidget children scene object (static
// Instance is set fresh per-scene in Awake) - the gameplay scene doesn't have one wired currently
// (an earlier scene revision, Assets/gamesceneBackup.unity, did - useful reference for rebuilding it).
public class ToastManager : MonoBehaviour
{
    public static ToastManager Instance;

    private ToastWidget[] _pool;
    private static string _pendingAfterSceneLoad;

    private void Awake()
    {
        Instance = this;
        _pool = GetComponentsInChildren<ToastWidget>(true);
        foreach (var widget in _pool)
            widget.gameObject.SetActive(false);
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
        var widget = _pool.FirstOrDefault(w => w.CanUse);
        if (widget == null)
        {
            LogHelper.Warn("Toast", $"Pool exhausted, dropping: {message}");
            return;
        }

        widget.Show(message);
    }

    // Queues a toast that shows after the next scene load - use when a scene change is about to
    // happen and the current ToastManager (and its pool) will be destroyed with it.
    public static void ShowAfterSceneLoad(string message)
    {
        _pendingAfterSceneLoad = message;
    }
}
