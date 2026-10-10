using Quantum;
using UnityEngine;

/// <summary>
/// Put on a window/panel that must stop gameplay input while it is open (ChooseWindow, BossWindow...): the
/// simulation gets empty input from the local player for as long as this object is active (see
/// <see cref="UiInputGate"/>). UiPopup does this by itself and needs no blocker. Ignored while the root Canvas
/// it sits under is switched off - the MenuScene Canvas stays active during a match and must not block it.
/// </summary>
public class UiInputBlockerWidget : MonoBehaviour
{
    private Canvas rootCanvas;

    private void Awake()
    {
        UiInputGate.Register(this, IsVisible);
    }

    private bool IsVisible()
    {
        if (rootCanvas == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>(true);
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }

        return rootCanvas == null || rootCanvas.enabled;
    }
}
