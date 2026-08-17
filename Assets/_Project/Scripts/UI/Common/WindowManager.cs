using System;
using QuantumUser.View.Util;
using UnityEngine;

public class WindowManager : MonoBehaviour
{
    public UiWindow currentWindow;
    public UiWindow[] uiWindows;
    public Action<UiWindow> onShow;
    public Action<UiWindow> onHide;

    private void Awake()
    {
        uiWindows = GetComponentsInChildren<UiWindow>(true);
        ForceAwakeOnInactiveWindows();
    }

    // GetComponentsInChildren(true) finds an inactive window's UiWindow component just fine, but
    // merely finding the reference does NOT run its own Awake() - Unity only calls Awake the
    // moment a GameObject first becomes active in hierarchy. A window that starts deactivated in
    // the scene (the normal setup - most windows should start hidden) would then have every one of
    // its own Awake-time setup (e.g. ChooseWindow cloning its card grid and wiring button
    // listeners) silently deferred until the FIRST time ShowWindow<T>() actually shows it - fragile
    // (anything that reads a window's state before its first real Show() sees pre-Awake nulls),
    // and it forces every window to stay active in the scene just to dodge the problem.
    //
    // Fixed by forcing each inactive window's Awake to run right now instead - toggling a
    // GameObject on then immediately back off runs Awake/OnEnable/OnDisable synchronously inside
    // that SetActive(true) call, before it even returns, so nothing actually renders a frame in
    // between (Unity presents a frame only after all scripts finish running, never mid-call) - a
    // window's own Awake already leaves everything in its correct final-hidden state by the time it
    // returns (see e.g. ChooseWindow.Awake's own cardPrefab.SetActive(false) at the end), so this
    // reliably leaves every window exactly where it already was, just pre-initialized. One caveat:
    // a child component with an OnEnable-triggered auto-play (e.g. a ParticleSystem with
    // playOnAwake, or an animation that restarts itself on enable) WILL briefly enable/disable
    // during this - verify nothing under any window prefab does that if something looks off after
    // this runs (ChooseWindow's own introParticles are explicitly designed around this exact
    // concern already, see its own field comment - other windows may not be).
    private void ForceAwakeOnInactiveWindows()
    {
        foreach (UiWindow uiWindow in uiWindows)
        {
            if (uiWindow.gameObject.activeSelf == false)
            {
                uiWindow.gameObject.SetActive(true);
                uiWindow.gameObject.SetActive(false);
            }
        }
    }

    public UiWindow ShowWindow<T>()
    {
        Type typeOfAction = typeof(T);

        LogHelper.Log("WindowManager", "Show windows =>" + typeOfAction.Name);
        UiWindow window = null;
        
        foreach (var uiWindow in uiWindows)
        {
            if ( !(uiWindow is T))
            {
                
                uiWindow.Hide();
                onHide?.Invoke(uiWindow);
            }
        }
        foreach (var uiWindow in uiWindows)
        {
            if (uiWindow is T)
            {
                uiWindow.Show();
                onShow?.Invoke(uiWindow);
                window = uiWindow;
            }
        }

        currentWindow = window;
        return window;
        
    }
}
