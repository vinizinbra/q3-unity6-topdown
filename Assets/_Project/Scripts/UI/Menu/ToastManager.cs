using System.Linq;
using QuantumUser.View.Util;
using UnityEngine;

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
