using System;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
public class ChangeNamePopup : UiPopup {

    public static ChangeNamePopup instance;
    public TMP_InputField nameInput;
    [CanBeNull] public System.Action callback;
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
    public void Confirm()
    {
        //PlayerInfo.Instance.SetPlayerName(nameInput.text);
        base.Close();
        callback?.Invoke();
        callback = null;
    }
    
}
