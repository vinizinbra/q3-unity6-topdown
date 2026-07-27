using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public class PgPopupManager : PgSingleton<PgPopupManager> {

    [SerializeField] private  List<PgUiPopupBase> popups = new List<PgUiPopupBase>();
    [SerializeField] private  List<PgUiPopupBase> popupQueue = new List<PgUiPopupBase>();
    [SerializeField] private  Image dimBg;
    [SerializeField] private float showDuration;
    [SerializeField] private  float hideDuration;
    public PgUiPopupBase currentPopup;
    public GameObject playButton;
    public Action<PgUiPopupBase> onShow;
    public Action<PgUiPopupBase> onHide;
    private readonly Stack<PgUiPopupBase> popupStack = new();

    public void AddPopupToList(PgUiPopupBase popup)
    {
        popups.Add(popup);
        popup.gameObject.SetActive(true);
        popup.onClose += OnPopupClose;
    }
    public int popupOnTopCount => popupStack.Count;
    public bool HasPendingPopups => currentPopup != null || popupQueue.Count > 0;
    public void CloseCurrentPopup()
    {
        if (currentPopup != null && !currentPopup.blockExternalClose && !currentPopup.isClosing)
        {
            StopAllCoroutines();
            currentPopup.Close();
        }
    }

    protected override void OnInit()
    {
        base.OnInit();
        popups = GetComponentsInChildren<PgUiPopupBase>(true).ToList();
        foreach (PgUiPopupBase ui in popups)
        {
            ui.gameObject.SetActive(true);
            ui.onClose += OnPopupClose;
        }
    }

    public void ShowPopupOnTop(PgUiPopupBase popup, bool blockExternalClose = false)
    {
        if (currentPopup != null)
        {
            popupStack.Push(currentPopup);
            currentPopup.gameObject.SetActive(false);
        }

        popup.blockExternalClose = blockExternalClose;
        popup.isClosing = false;
        currentPopup = popup;
        popup.transform.SetAsLastSibling();
        popup.gameObject.SetActive(true);
        popup.Show();
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
        HideDim();
        currentPopup = null;
    }

    public void AddPopupToQueue(PgUiPopupBase popup, bool blockExternalClose = false)
    {
        popup.blockExternalClose = blockExternalClose;
        popupQueue.Add(popup);
    }    
    
    public void ShowPopup(PgUiPopupBase pgUiPopup)
    {
        if (pgUiPopup == currentPopup) return;
        pgUiPopup.isClosing = false;
        currentPopup = pgUiPopup;
        ShowDim();
        foreach (PgUiPopupBase ui in popups)
        {
            if (ui.gameObject.Equals(pgUiPopup.gameObject))
            {
                pgUiPopup.gameObject.SetActive(true);
                ui.Show();
            }
        }
    }

    void ShowDim()
    {
        StopAllCoroutines();
        dimBg.raycastTarget = true;
        StartCoroutine(ShowDimCoroutine(0,0.8f));
    }
    void HideDim()
    {
        StopAllCoroutines();
        StartCoroutine(ShowDimCoroutine(0.8f,0));
        dimBg.raycastTarget = false;

    }
    

    IEnumerator ShowDimCoroutine(float start, float end)
    {
        float t = 0;
        while (t<1)
        {
            t += Time.deltaTime * 10;
            var alpha = Mathf.MoveTowards(start, end, t);
            dimBg.color = new Color(1, 1, 1, alpha);
            yield return null;
        }
        dimBg.color = new Color(1, 1, 1, end);

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
        if (currentPopup == null) return;
        if (currentPopup.isClosing && currentPopup.uiTweens.Length > 0 && !currentPopup.ignoreTweens) return;

        currentPopup.isClosing = true;

        if (currentPopup.uiTweens.Length <= 0 || currentPopup.ignoreTweens)
        {
            CheckPopup();
            return;
        }

        foreach (var uiTween in currentPopup.uiTweens)
        {
            uiTween.PlayBackward();
            uiTween.onComplete += CheckPopup;
        }
    }

    private void CheckPopup()
    {
        if (currentPopup == null) return;
        if (currentPopup.uiTweens.Any(x => x.IsPlaying)) return;

        foreach (var uiTween in currentPopup.uiTweens)
            uiTween.onComplete -= CheckPopup;

        currentPopup.isClosing = false;
        currentPopup.gameObject.SetActive(false);

        if (popupStack.Count > 0)
        {
            currentPopup = popupStack.Pop();
            currentPopup.gameObject.SetActive(true);
            currentPopup.Show();
        }
        else
        {
            if (popupQueue.Count > 0)
                popupQueue.RemoveAt(0);
            HideDim();
            currentPopup = null;
        }
    }

    public void Clear()
    {
        popupQueue.Clear();
    }
}
