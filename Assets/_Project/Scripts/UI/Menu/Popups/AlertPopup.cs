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

    public override void Awake()
    {
        base.Awake();
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


    // Fires the pending callback no matter how the popup is dismissed - the OK button routes here
    // via Callback(), but PopupManager.CloseCurrentPopup() or a future back-button/backdrop-tap
    // handler can also call Close() directly. Every current caller's callback does something that
    // must run (e.g. disconnecting the client after an error ack), so a close path that skipped it
    // would leave that caller hanging.
    public override void Close()
    {
        var pending = callback;
        callback = null;
        base.Close();
        pending?.Invoke();
    }

    public void Callback()
    {
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