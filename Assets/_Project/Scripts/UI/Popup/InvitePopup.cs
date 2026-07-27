using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
public class InvitePopup : UiPopup {

    public static InvitePopup instance;
    public TMP_Text title;
    public TMP_Text description;
    [CanBeNull] public System.Action callback;
    public string from;
    public virtual void Awake()
    {
        instance = this;
    }


    public override void Close()
    {
        base.Close();
        callback?.Invoke();
        callback = null;
    }

    public void Accept()
    {
       // PhotonChatManager.instance.AcceptInviteFrom(from);
        Close();
    }
    public void Reject()
    {
        //PhotonChatManager.instance.RejectInviteFrom(from);
        Close();

    }
    public void Setup(string title,string description,string sender, System.Action callback = null)
    {
        this.title.text = title;
        this.description.text = description;
        this.callback = callback;
        this.from = sender;
        PopupManager.instance.AddPopupToQueue(this);
    }
}