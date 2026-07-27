using System.Linq;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class PgUiPopup<T> : PgUiPopupBase where T : PgUiPopup<T>
{
    public static T Instance { get; private set; }

    public GameObject[] hide;

    public virtual void Awake()
    {
        Instance = this as T;
        uiTweens = GetComponentsInChildren<UiTween>(true);
        uiTweens = uiTweens.Where(x => x.playType == UiTween.UiPlayType.ONCE && x.transform.GetComponent<IgnoreTween>() == null).ToArray();
    }

    public override void Close(bool ignoreTweens = false)
    {
        this.ignoreTweens = ignoreTweens;
        onClose?.Invoke();
        hide.SetActive(true);
    }
    public void CloseInstant()
    {
        Close(ignoreTweens: true);
    }

    public override void Show()
    {
        onShow?.Invoke();
        hide.SetActive(false);
    }
    
    [Button]
    public void AddPopup()
    {
        PgPopupManager.I.AddPopupToQueue(this);    
    }

}