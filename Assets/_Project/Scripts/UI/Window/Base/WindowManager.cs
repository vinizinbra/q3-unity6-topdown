using System;
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
    }
    
    public UiWindow ShowWindow<T>()
    {
        Type typeOfAction = typeof(T);

        Debug.Log("Show windows =>"
                  +typeOfAction.Name);
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
