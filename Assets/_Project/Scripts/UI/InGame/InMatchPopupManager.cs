using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

// Scene-local twin of PopupManager (Assets/_Project/Scripts/UI/Common/PopupManager.cs), living in
// GrasslandOutpostGameScene instead of the persistent MenuScene. MenuScene never unloads and
// GrasslandOutpostGameScene loads/unloads additively on top of it for each match, so a single
// static-instance manager can't safely serve both: whichever scene's Awake ran last would win
// PopupManager.instance, and after the gameplay scene unloads that reference would go stale with
// nothing to reset it back to the menu's manager. Being a separate class with its own static
// instance sidesteps that entirely - this one gets destroyed and recreated with the scene every
// match, which is also the behavior we want (no popup/queue state should survive into the next run).
public class InMatchPopupManager : MonoBehaviour {

    public UiPopup[] popups;
    public UiPopup currentPopup;
    public static InMatchPopupManager instance;
    public List<UiPopup> popupQueue;

    [SerializeField] private Image dimBg;
    [SerializeField] private float dimFadeDuration = 0.15f;
    [SerializeField] private float dimAlpha = 0.8f;
    private Tween dimTween;

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

    public void Clear()
    {
        popupQueue.Clear();
    }

    public void CloseCurrentPopup()
    {
        if (currentPopup != null && !currentPopup.blockExternalClose)
            currentPopup.Close();
    }

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
        if (popup == currentPopup || popupQueue.Contains(popup)) return;
        popupQueue.Add(popup);
    }

    // Queues the registered popup of type T by type instead of requiring a direct scene reference
    // (see InMatchTutorialManager) - looks it up in `popups` (populated once in Awake from
    // GetComponentsInChildren), so T must actually be a child of this manager. Returns null (and
    // queues nothing) if no such popup is registered.
    public T Open<T>() where T : UiPopup
    {
        foreach (var popup in popups)
        {
            if (popup is T typed)
            {
                AddPopupToQueue(popup);
                return typed;
            }
        }

        return null;
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
        if (currentPopup == null && popupQueue.Count > 0)
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
