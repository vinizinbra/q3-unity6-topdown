using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TabButtonRectScale : MonoBehaviour
{
    public RectTransform rectTransform;
    public Vector2 previousSize;
    public Vector2 activeSize;
    
    void Start()
    {
        var tabButton = GetComponent<TabButton>();
        tabButton.onTabSelected.AddListener(Select);
        tabButton.onTabDeselected.AddListener(Deselect);
    }
    private void Reset()
    {
        rectTransform = GetComponent<RectTransform>();
        previousSize = rectTransform.sizeDelta;

    }
    
    public void Select()
    {
        rectTransform.sizeDelta = activeSize;
    }
    public void Deselect()
    {
        rectTransform.sizeDelta = previousSize;
    }
}
