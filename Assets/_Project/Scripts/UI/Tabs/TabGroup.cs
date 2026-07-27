using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        if (selectedTabButton != null)
        {
            selectedTabButton.Deselect();
        }
        
        selectedTabButton = button;
        
        selectedTabButton.Select();
        
        ResetTabs();
        button.background.color = tabActive;
        int index = button.transform.GetSiblingIndex();
        for (int i = 0; i < tabContent.Count; i++)
        {
            if(tabContent[i].gameObject.activeSelf)
                tabContent[i].Hide();
            
            tabContent[i].gameObject.SetActive(false);
            
        }

        if (tabContent.Count > index)
        {
            tabContent[index].gameObject.SetActive(true);
            selectedTabContent = tabContent[index];
            tabContent[index].Show();
        }
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
