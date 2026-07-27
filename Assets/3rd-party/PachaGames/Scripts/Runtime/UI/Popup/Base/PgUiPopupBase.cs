using System;
using UnityEngine;

public abstract class PgUiPopupBase : MonoBehaviour
{
    public abstract void Show();
    public abstract void Close(bool ignoreTweens = false);
    public Action onShow;
    public Action onClose;
    public UiTween[] uiTweens;
    public bool ignoreTweens = false;
    public bool blockExternalClose = false;
    [System.NonSerialized] public bool isClosing;

}