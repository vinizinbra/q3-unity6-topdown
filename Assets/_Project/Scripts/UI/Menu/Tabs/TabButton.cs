using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TabButton : MonoBehaviour, IPointerClickHandler,IPointerEnterHandler,IPointerExitHandler,ISubmitHandler,ISelectHandler,IMoveHandler
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

    // Gamepad/keyboard focus (not a mouse press) opens the tab as a preview: moving down the rail shows what
    // each tab holds, and Submit or Right then steps into it. The focus stays on this button.
    public void OnSelect(BaseEventData eventData)
    {
        if (eventData is PointerEventData)
            return;

        group.PreviewTab(this);
    }

    public void OnMove(AxisEventData eventData)
    {
        if (eventData.moveDir != MoveDirection.Right || !group.HasContent(this))
            return;

        group.OnTabSelected(this, true);
        eventData.Use();
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