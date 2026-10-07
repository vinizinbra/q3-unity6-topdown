using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class TabGroup : MonoBehaviour
{
    public List<TabButton> tabButtons;
    public List<TabContent> tabContent;

    public Color tabIdle;
    public Color tabHover;
    public Color tabActive;
    [FormerlySerializedAs("selectedTab")] public TabButton selectedTabButton;
    public TabContent selectedTabContent;
    
    private void Start()
    {
        if (selectedTabButton != null)
        {
            OnTabSelected(selectedTabButton);
        }
    }

    public void Subscribe(TabButton button)
    {
        if (tabButtons == null)
            tabButtons = new List<TabButton>();
    }

    public void OnTabEnter(TabButton button)
    {
        ResetTabs();
        if (selectedTabButton == null || button != selectedTabButton)
        {
            button.background.color = tabHover;
        }
    }
    
    public void OnTabExit(TabButton button)
    {
        ResetTabs();

    }
    
    public void OnTabSelected(TabButton button)
    {
        OnTabSelected(button, false);
    }

    public void OnTabSelected(TabButton button, bool focusContent)
    {
        if (selectedTabButton != null)
        {
            selectedTabButton.Deselect();
        }
        
        selectedTabButton = button;
        
        selectedTabButton.Select();
        
        ResetTabs();
        button.background.color = tabActive;
        // Paired with the content list by position in tabButtons, not sibling index: buttons share
        // their parent with non-button siblings (the logo), which would shift every index.
        int index = tabButtons.IndexOf(button);
        for (int i = 0; i < tabContent.Count; i++)
        {
            if (tabContent[i] == null)
                continue;

            if(tabContent[i].gameObject.activeSelf)
                tabContent[i].Hide();
            
            tabContent[i].gameObject.SetActive(false);
            
        }

        // A button with no content yet (index past the list, or an empty slot) just shows nothing.
        if (index >= 0 && tabContent.Count > index && tabContent[index] != null)
        {
            tabContent[index].gameObject.SetActive(true);
            selectedTabContent = tabContent[index];
            tabContent[index].Show();

            if (focusContent)
                StartCoroutine(FocusContentNextFrame(tabContent[index], button));
        }
    }

    // Gamepad: after opening a tab by Submit, focus moves into it (its DefaultFocus, else the first
    // interactable). Deferred a frame so the content's own Show()/layout has settled.
    private IEnumerator FocusContentNextFrame(TabContent content, TabButton button)
    {
        yield return null;

        if (content == null || !content.gameObject.activeInHierarchy)
            yield break;

        // A tab with a focus scope hands the whole level over (Cancel then returns to this tab's button).
        FocusScopeWidget scope = content.GetComponent<FocusScopeWidget>();
        if (scope != null)
        {
            scope.Enter(button != null ? button.GetComponent<Selectable>() : null);
            yield break;
        }

        Selectable target = content.DefaultFocus;
        if (target == null || !target.interactable || !target.gameObject.activeInHierarchy)
        {
            target = null;
            foreach (Selectable selectable in content.GetComponentsInChildren<Selectable>(false))
            {
                if (selectable.interactable)
                {
                    target = selectable;
                    break;
                }
            }
        }

        UiSelectionUtility.SelectFirstInteractable(target);
    }
    public TabContent SelectTab<T>()
    {
        TabContent content = null;
        if (selectedTabButton != null)
        {
            selectedTabButton.Deselect();
        }

        for (int i = 0; i < tabContent.Count; i++)
        {
            if (tabContent[i] is T)
            {
                if (tabButtons.Count > i)
                {
                    selectedTabButton = tabButtons[i];
                    selectedTabButton.Select();

                }
                break;
            }
        }
        
        ResetTabs();
        
        for (int i = 0; i < tabContent.Count; i++)
        {
            if(tabContent[i].gameObject.activeSelf)
                tabContent[i].Hide();

            tabContent[i].gameObject.SetActive(false);
            
            if (tabContent[i] is T)
            {
                tabContent[i].gameObject.SetActive(true);
                tabContent[i].Show();
                selectedTabContent = tabContent[i];
                content = tabContent[i];
            }
        }

        return content;


    }

    public TabContent GetTab<T>()
    {
        TabContent content = null;

        for (int i = 0; i < tabContent.Count; i++)
        {
            if (tabContent[i] is T)
            {
                content = tabContent[i];
            }
        }

        return content;
    }
    public void ResetTabs()
    {
        foreach (var button in tabButtons)
        {
            if(selectedTabButton != null && button == selectedTabButton)
                continue;
            button.background.color = tabIdle;
        }
    }
}
