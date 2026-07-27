using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class TabContent : MonoBehaviour
{
    public GameObject[] objectsToHide;
    public static TabContent Instance;

    protected virtual void Awake()
    {
        Instance = this;
    }

    public void SetObjects(bool active)
    {
        foreach (var obj in objectsToHide)
        {
            obj.SetActive(active);
        }
    }

    public void Show()
    {
        SetObjects(false);
        OnShow();
    }

    public void Hide()
    {
        SetObjects(true);
        OnHide();
    }
    protected abstract void OnShow();
    protected abstract void OnHide();

}
