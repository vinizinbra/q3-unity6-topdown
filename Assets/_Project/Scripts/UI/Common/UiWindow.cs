using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class UiWindow : MonoBehaviour
{
    public static UiWindow Instance;
    public GameObject[] hide;
    public System.Action onShow;
    public System.Action onHide;
    private void Awake()
    {
        Instance = this;
    }

    public virtual void Show()
    {
        onShow?.Invoke();

        gameObject.SetActive(true);

        foreach (var go in hide)
        {
            go.SetActive(false);
        }

        AutoSelectFirstInteractable();
    }

    // Focuses the first active+interactable Selectable under this window for gamepad/joystick
    // navigation, one frame after Show() (so a subclass's own Show() override - which may still be
    // tweaking button active-state after its own base.Show() call - has finished first) and, if
    // that target plays a ShakeGrowImpactAnimation pop-in, further deferred until that animation
    // has landed - see UiSelectionUtility. Override to no-op for a window that resolves its own
    // target more specifically (e.g. ChooseWindow, which picks between two card families) or has
    // nothing worth selecting.
    protected virtual void AutoSelectFirstInteractable()
    {
        StartCoroutine(UiSelectionUtility.SelectFirstInteractableNextFrame(this));
    }

    public virtual void Hide()
    {
        onHide?.Invoke();

        gameObject.SetActive(false);
        foreach (var go in hide)
        {
            go.SetActive(true);
        }
    }
}
