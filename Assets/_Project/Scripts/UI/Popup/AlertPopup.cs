using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

public class AlertPopup : UiPopup
{

    public static AlertPopup instance;
    public TMP_Text title;
    public TMP_Text description;
    [CanBeNull] public System.Action callback;

    public virtual void Awake()
    {
        instance = this;
    }


    public override void Close()
    {
        base.Close();
    }

    public void  Callback()
    {
        callback?.Invoke();
        callback = null;
        Close();
    }

public void Setup(string title,string description, System.Action callback = null)
    {
        this.title.text = title;
        this.description.text = description;
        this.callback = callback;
        PopupManager.instance.AddPopupToQueue(this);
    }
}