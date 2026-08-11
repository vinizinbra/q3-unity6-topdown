using System;
using NaughtyAttributes;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class UiPopup : MonoBehaviour
{

    public Action onShow;
    public Action onClose;
    public CanvasGroup canvasGroup;
    
    public bool hide;
    public float hideSpeed = 10;

    [Button]
    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public virtual void Close()
    {
        hide = true;
        onClose?.Invoke();
    }

    public virtual void Show()
    {
        canvasGroup.alpha = 1;
        hide = false;
        gameObject.SetActive(true);
        onShow?.Invoke();
    }

    public virtual void Update()
    {
        if (hide)
        {
            canvasGroup.alpha -= Time.unscaledDeltaTime * hideSpeed;
            if (canvasGroup.alpha <= 0)
            {
                gameObject.SetActive(false);
            }
        }
    }
    [Button]
    protected void AddPopup()
    {
        PopupManager.instance.AddPopupToQueue(this);    
    }

}