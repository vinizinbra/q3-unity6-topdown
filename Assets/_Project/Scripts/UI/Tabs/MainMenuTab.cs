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

    protected override void OnShow()
    {
    }

    protected override void OnHide()
    {
    }
}
