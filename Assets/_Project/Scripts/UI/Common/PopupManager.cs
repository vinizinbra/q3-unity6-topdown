using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PopupManager : MonoBehaviour {

    public UiPopup[] popups;
    public UiPopup currentPopup;
    public static PopupManager instance;
    public List<UiPopup> popupQueue;
    
    private void Awake()
    {
        instance = this;
        popups = GetComponentsInChildren<UiPopup>(true);
        foreach (UiPopup ui in popups)
        {
            ui.gameObject.SetActive(true);
            ui.onClose += OnPopupClose;
        }
    }

    private void Start()
    {
        HideAll();
    }

    public void HideAll()
    {
        foreach (var p in popups)
        {
            p.gameObject.SetActive(false);
        }

        currentPopup = null;
    }

    public void AddPopupToQueue(UiPopup popup)
    {
        // Every popup here is a single reused instance (e.g. the one AlertPopup), so a burst of
        // Show() calls for one logical event queues the SAME object more than once - the classic
        // case being a failed connect firing both HandleConnectFailure ("Connection Failed") and
        // OnDisconnected ("Disconnected"). Each redundant enqueue makes the popup pop straight back
        // up after the player dismisses it, reading as "two alerts". Collapse duplicates: the latest
        // Setup already overwrote the shared title/description/callback, so the one queued entry
        // shows the most recent message.
        if (popup == currentPopup || popupQueue.Contains(popup)) return;
        popupQueue.Add(popup);
    }
    
    public void ShowPopup(UiPopup uiPopup)
    {
        if (uiPopup == currentPopup) return;
        currentPopup = uiPopup;
        foreach (UiPopup ui in popups)
        {
            if (ui.gameObject.Equals(uiPopup.gameObject))
            {
                ui.Show();
            }
        }
    }

    private void Update()
    {
        if (currentPopup == null && popupQueue.Count>0)
        {
            ShowPopup(popupQueue[0]);
        }
    }

    void OnPopupClose()
    {
        popupQueue.RemoveAt(0);
        currentPopup = null;
    }
}
