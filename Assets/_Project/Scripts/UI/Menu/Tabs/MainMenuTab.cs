using UnityEngine;
using Quantum;
using TMPro;

public class MainMenuTab : TabContent
{
    public WindowManager windowManager;
    public TMP_InputField TMPText;
    void Start()
    {
        OpenMain();
    }

    public void OpenMain()
    {
        windowManager.ShowWindow<MainMenuWindow>();
        
    }
    public void OpenChangeName()
    {
        PopupManager.instance.AddPopupToQueue(ChangeNamePopup.instance);        
    }

    // Entering the Home tab (Submit / Right on its rail button) lands on the Play button.
    public override UnityEngine.UI.Selectable DefaultFocus
    {
        get
        {
            var window = FindFirstObjectByType<MainMenuWindow>(FindObjectsInactive.Exclude);
            return window != null && window.playButton != null && window.playButton.IsActive() && window.playButton.IsInteractable() ? window.playButton : null;
        }
    }

    // Right on the Home rail button lands on the hero picker (the control nearest the rail); Submit goes to Play.
    public override UnityEngine.UI.Selectable RightFocus
    {
        get
        {
            var party = FindFirstObjectByType<PartyRoomWidget>(FindObjectsInactive.Exclude);
            var hero = party != null ? party.characterDropdown : null;
            return hero != null && hero.IsActive() && hero.IsInteractable() ? hero : DefaultFocus;
        }
    }

    protected override void OnShow()
    {
    }

    protected override void OnHide()
    {
    }
}
