using System.Collections.Generic;
using UnityEngine;

// Drives every live TextBatchOptimizer's Sync once per frame.
//
// Two reasons this is one shared driver rather than an Update on each optimizer: a hoisted text has
// to be repositioned AFTER the widget that owns its placeholder has moved for the frame, and the
// widgets here all move in their own LateUpdate (CharacterUiWidget.FollowTarget,
// DamageNumberUiWidget.RefreshAnchoredPosition). Per-component ordering between MonoBehaviours is
// undefined, so the sync would lag a frame behind at random; DefaultExecutionOrder below pins this
// driver last instead.
//
// It creates itself on first registration - deliberately not a scene object, since a manager that
// has to be remembered in the scene is exactly the wiring this codebase keeps losing track of, and
// an unwired one here would silently strand every hoisted text at the origin.
[DefaultExecutionOrder(1000)]
public class TextBatchOptimizerManager : MonoBehaviour
{
    private static TextBatchOptimizerManager _instance;

    private readonly List<TextBatchOptimizer> _tracked = new List<TextBatchOptimizer>();

    public static void Register(TextBatchOptimizer optimizer)
    {
        EnsureInstance();

        if (_instance._tracked.Contains(optimizer) == false)
            _instance._tracked.Add(optimizer);
    }

    public static void Unregister(TextBatchOptimizer optimizer)
    {
        if (_instance == null)
            return;

        _instance._tracked.Remove(optimizer);
    }

    private static void EnsureInstance()
    {
        if (_instance != null)
            return;

        var host = new GameObject(nameof(TextBatchOptimizerManager));
        _instance = host.AddComponent<TextBatchOptimizerManager>();
    }

    // Backwards, so an optimizer destroyed this frame drops out without disturbing the walk.
    private void LateUpdate()
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            TextBatchOptimizer optimizer = _tracked[i];

            if (optimizer == null)
            {
                _tracked.RemoveAt(i);
                continue;
            }

            optimizer.Sync();
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
