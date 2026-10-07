using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TabButton : MonoBehaviour, IPointerClickHandler,IPointerEnterHandler,IPointerExitHandler,ISubmitHandler
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

    // Gamepad/keyboard: needs a Selectable on this object to receive focus (added next to this component).
    // Unlike a click, opening by Submit also hands focus to the new tab's content.
    public void OnSubmit(BaseEventData eventData)
    {
        group.OnTabSelected(this, true);
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