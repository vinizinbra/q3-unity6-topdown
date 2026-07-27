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
