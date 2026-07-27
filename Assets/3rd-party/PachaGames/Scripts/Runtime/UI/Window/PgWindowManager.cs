using System;
using UnityEngine;

namespace PG
{
    public class PgWindowManager : MonoBehaviour
    {
        [SerializeField] private UiWindowBase[] registeredWindows; // Manually assigned or auto-filled

        public UiWindowBase currentWindow;
        public Action<UiWindowBase> onShow;
        public Action<UiWindowBase> onHide;

        public UiWindowBase firstWindow;

        private void Awake()
        {
            if (registeredWindows == null || registeredWindows.Length == 0)
            {
                registeredWindows = GetComponentsInChildren<UiWindowBase>(true);
                foreach (var windows in registeredWindows)
                {
                    Debug.Log(windows.name + " Awake");

                    windows.Awake();
                }
            }
        }

        private void Start()
        {
            if (firstWindow != null)
                Invoke(nameof(ShowFirstWindow), 0.5f);
        }

        private void ShowFirstWindow()
        {
            ShowWindow(firstWindow);
        }

        public T ShowWindow<T>() where T : UiWindowBase
        {
            T foundWindow = null;
            
            HideAll();
            foreach (var uiWindow in registeredWindows)
            {
                if (uiWindow is T typedWindow)
                {
                    uiWindow.Show();
                    onShow?.Invoke(uiWindow);
                    foundWindow = typedWindow;
                    break;
                }
            }

            currentWindow = foundWindow;

            return foundWindow;
        }

        public UiWindowBase ShowWindow(UiWindowBase windowToShow)
        {
            UiWindowBase foundWindow = null;

            HideAll();

            foreach (var uiWindow in registeredWindows)
            {
                if (uiWindow == windowToShow)
                {
                    uiWindow.Show();
                    onShow?.Invoke(uiWindow);
                    foundWindow = uiWindow;
                    break;
                }
            }

            currentWindow = foundWindow;
            return foundWindow;
        }

        public void HideAll()
        {
            foreach (var uiWindow in registeredWindows)
            {
                uiWindow.Hide();
                onHide?.Invoke(uiWindow);
            }

            currentWindow = null;
        }
    }

}
