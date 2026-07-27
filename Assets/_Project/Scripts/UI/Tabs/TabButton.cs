using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TabButton : MonoBehaviour, IPointerClickHandler,IPointerEnterHandler,IPointerExitHandler
{
    public TabGroup group;
    public Image background;
    public UnityEvent onTabSelected;
    public UnityEvent onTabDeselected;
    private void Start()
    {
        group.Subscribe(this);
        
        if(background == null)
            background = GetComponent<Image>();
    }

    private void Reset()
    {
        background = GetComponent<Image>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        group.OnTabSelected(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        group.OnTabEnter(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        group.OnTabExit(this);
    }

    public void Select()
    {
        onTabSelected?.Invoke();
    }

    public void Deselect()
    {
        onTabDeselected?.Invoke();
    }
}