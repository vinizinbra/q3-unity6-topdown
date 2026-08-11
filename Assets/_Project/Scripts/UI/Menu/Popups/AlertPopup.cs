using System;
using JetBrains.Annotations;
using QuantumUser.View.Util;
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

    public static void Show(string title, string description, System.Action callback = null)
    {
        if (instance == null)
        {
            LogHelper.Warn("AlertPopup", $"instance is null, couldn't show '{title}': {description}");
            callback?.Invoke();
            return;
        }

        instance.Setup(title, description, callback);
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