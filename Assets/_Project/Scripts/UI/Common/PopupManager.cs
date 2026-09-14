using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public class PopupManager : MonoBehaviour {

    public UiPopup[] popups;
    public UiPopup currentPopup;
    public static PopupManager instance;
    public List<UiPopup> popupQueue;

    // Optional - wire an Image up in the Editor to get a modal dim behind popups (same
    // Tween.Alpha idiom as ChooseWindow's dimImage). Left unassigned, dimming is just skipped.
    [SerializeField] private Image dimBg;
    [SerializeField] private float dimFadeDuration = 0.15f;
    [SerializeField] private float dimAlpha = 0.8f;
    private Tween dimTween;

    // Popups pushed aside by ShowPopupOnTop (e.g. a confirmation over an already-open settings
    // popup) - not part of the FIFO popupQueue, so closing the top one restores whatever was
    // pushed instead of advancing the queue.
    private readonly Stack<UiPopup> popupStack = new();

    public int popupOnTopCount => popupStack.Count;
    public bool HasPendingPopups => currentPopup != null || popupQueue.Count > 0;

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

        popupStack.Clear();
        HideDim();
        currentPopup = null;
    }

    // Cancels every not-yet-shown queued popup (e.g. on scene teardown / logout); does not touch
    // whatever is currently on screen.
    public void Clear()
    {
        popupQueue.Clear();
    }

    // Safe entry point for a backdrop-tap or back-button handler: no-ops while the current popup
    // opted out via blockExternalClose instead of every caller having to check that flag itself.
    public void CloseCurrentPopup()
    {
        if (currentPopup != null && !currentPopup.blockExternalClose)
            currentPopup.Close();
    }

    // Shows `popup` above whatever is currently open without closing it - the pushed popup is just
    // deactivated (no onClose, no queue advance) and reactivated once `popup` closes. Use for
    // nested modals, e.g. a confirmation dialog opened from within a settings popup.
    public void ShowPopupOnTop(UiPopup popup, bool blockExternalClose = false)
    {
        if (currentPopup != null)
        {
            popupStack.Push(currentPopup);
            currentPopup.gameObject.SetActive(false);
        }

        popup.blockExternalClose = blockExternalClose;
        currentPopup = popup;
        popup.transform.SetAsLastSibling();
        ShowDim();
        popup.gameObject.SetActive(true);
        popup.Show();
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
        ShowDim();
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
        if (popupStack.Count > 0)
        {
            currentPopup = popupStack.Pop();
            currentPopup.gameObject.SetActive(true);
            currentPopup.Show();
            return;
        }

        // Guard against removing the wrong entry if a close ever fires while currentPopup wasn't
        // actually the head of the queue (e.g. CloseInstant called during teardown).
        if (popupQueue.Count > 0 && popupQueue[0] == currentPopup)
            popupQueue.RemoveAt(0);

        HideDim();
        currentPopup = null;
    }

    void ShowDim()
    {
        if (dimBg == null) return;
        dimBg.raycastTarget = true;
        dimTween.Stop();
        dimTween = Tween.Alpha(dimBg, dimBg.color.a, dimAlpha, dimFadeDuration, useUnscaledTime: true);
    }

    void HideDim()
    {
        if (dimBg == null) return;
        dimBg.raycastTarget = false;
        dimTween.Stop();
        dimTween = Tween.Alpha(dimBg, dimBg.color.a, 0f, dimFadeDuration, useUnscaledTime: true);
    }
}
