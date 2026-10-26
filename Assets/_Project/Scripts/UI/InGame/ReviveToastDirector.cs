using Quantum;
using QuantumUser.View;
using UnityEngine;

/// <summary>
/// Self-installed (same pattern as HapticsDirector): pops a "Revive canceled" toast for the local reviver whose hold
/// was broken by being pushed out of range (EventReviveCanceled, raised by ReviveChannelSystem). Not shown on a
/// deliberate button release.
/// </summary>
public class ReviveToastDirector : MonoBehaviour
{
    private static ReviveToastDirector instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        var go = new GameObject(nameof(ReviveToastDirector));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ReviveToastDirector>();
    }

    private void Awake()
    {
        QuantumEvent.Subscribe<EventReviveCanceled>(this, OnReviveCanceled);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    private void OnReviveCanceled(EventReviveCanceled e)
    {
        if (MyLocalPlayer.Instance == null)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet && slots[i].EntityRef == e.Reviver)
            {
                ToastManager.Instance?.Show("Revive canceled");
                return;
            }
        }
    }
}
