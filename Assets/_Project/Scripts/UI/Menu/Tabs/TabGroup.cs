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

        StartCoroutine(PrewarmClosedTabs());
    }

    // Tabs build their lists the first time they are shown (Catalog ~50 tiles, Heroes + 3D preview...), a visible
    // hitch the first time focus passes over them now that moving down the rail previews each tab. Shortly after
    // the menu opens, each closed tab is switched on invisibly (alpha 0) for two frames - enough for its Start /
    // first build - and off again, one tab per step so the work is spread out. Show()/Hide() are not called.
    private IEnumerator PrewarmClosedTabs()
    {
        yield return new WaitForSecondsRealtime(1f);

        foreach (TabContent content in tabContent)
        {
            if (content == null || content.gameObject.activeSelf)
                continue;

            CanvasGroup fade = content.GetComponent<CanvasGroup>();
            if (fade == null)
                fade = content.gameObject.AddComponent<CanvasGroup>();

            fade.alpha = 0f;
            content.gameObject.SetActive(true);
            yield return null;
            yield return null;

            // The player may have opened this very tab meanwhile - then it must stay on.
            if (content != null && content != selectedTabContent)
                content.gameObject.SetActive(false);

            fade.alpha = 1f;
            yield return null;
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

    /// <summary>True when this button has a content to show (a rail button past the end of the list has none).</summary>
    public bool HasContent(TabButton button)
    {
        int index = tabButtons.IndexOf(button);
        return index >= 0 && index < tabContent.Count && tabContent[index] != null;
    }

    /// <summary>
    /// Focus moved onto a tab button (gamepad/keyboard): open its content as a preview without taking focus
    /// off the button. Buttons without content, and the tab that is already open, are left alone.
    /// </summary>
    public void PreviewTab(TabButton button)
    {
        if (HasContent(button))
            OnTabSelected(button, false);
    }

    public void OnTabSelected(TabButton button, bool focusContent)
    {
        // Already open (focus came back to its button, or Submit/Right on it): don't hide and re-show the
        // content - that restarts its intro animations - just hand over focus if asked.
        if (selectedTabButton == button && selectedTabContent != null && selectedTabContent.gameObject.activeSelf)
        {
            if (focusContent)
                StartCoroutine(FocusContentNextFrame(selectedTabContent, button));
            return;
        }

        if (selectedTabButton != null)
        {
            selectedTabButton.Deselect();
        }
        
        bool hadContent = selectedTabContent != null;
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

            // The tab that comes in rises and fades in; skipped for the tab that is already open (re-select) and the first open.
            if (hadContent)
                PageTransitionWidget.Play(tabContent[index].gameObject);

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
