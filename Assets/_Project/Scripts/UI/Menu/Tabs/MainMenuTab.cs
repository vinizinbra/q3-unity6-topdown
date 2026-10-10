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

    protected override void OnShow()
    {
    }

    protected override void OnHide()
    {
    }
}
