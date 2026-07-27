using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class UiWindow : MonoBehaviour
{
    public static UiWindow Instance;
    public GameObject[] hide;
    public System.Action onShow;
    public System.Action onHide;
    private void Awake()
    {
        Instance = this;
    }

    public virtual void Show()
    {
        onShow?.Invoke();
        
        gameObject.SetActive(true);
        
        foreach (var go in hide)
        {
            go.SetActive(false);
        }
    }

    public virtual void Hide()
    {
        onHide?.Invoke();

        gameObject.SetActive(false);
        foreach (var go in hide)
        {
            go.SetActive(true);
        }
    }
}
