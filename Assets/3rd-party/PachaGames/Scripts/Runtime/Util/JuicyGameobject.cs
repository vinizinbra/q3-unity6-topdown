using System;
using NaughtyAttributes;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

public class JuicyGameobject : MonoBehaviour
{
    private Vector3 initialScale = Vector3.one;
    public float duration = 0.3f;
    public float delay = 0.0f;
    public float hideDuration = 0.3f;
    public Ease showEase = Ease.OutBack;
    public Ease hideEase = Ease.InBack;
    public bool hiding = false;
    public bool hideOnStart = false;
    public UnityEvent onHide = new UnityEvent();
    public UnityEvent onShow = new UnityEvent();
    private void Awake()
    {            
        if (hideOnStart)
        {
            gameObject.SetActive(false);
        }
    }

    public void SetActive(bool active)
    {
        if(active)
            Show();
        else
        {
            Hide();
        }
    }
    [Button]
    public void Show()
    {
        if(gameObject.activeSelf)return;
        transform.localScale = Vector3.zero;
        gameObject.SetActive(true);
        Tween.Delay(gameObject,delay).OnComplete(() =>
        {
            Tween.Scale(transform, initialScale, duration, showEase).OnComplete(() => onShow?.Invoke());
        });
    }

    [Button]
    public void Hide()
    {
        if (hiding) return;
        if (!gameObject.activeSelf) return;
        
        hiding = true;
        
        PrimeTween.Tween.Scale(transform, Vector3.zero, hideDuration, hideEase).OnComplete(() =>
        {
            hiding = false;
            gameObject.SetActive(false);
            onHide?.Invoke();
        });
    }
}
