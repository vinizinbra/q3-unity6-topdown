using System;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class UiPopup : MonoBehaviour
{

    public Action onShow;
    public Action onClose;
    public CanvasGroup canvasGroup;

    // Optional - wired straight to Close() here instead of every popup prefab needing its own
    // Button.onClick -> Close()/Callback() Inspector wiring (or a bespoke script) just to dismiss
    // itself. Left unassigned, a subclass is free to wire its own button(s) to whatever it needs
    // (e.g. AlertPopup/ChangeNamePopup's own Confirm/Cancel actions).
    [SerializeField] private Button closeButton;

    public bool hide;
    public float hideSpeed = 10;

    // Set by whoever opens this popup (see PopupManager.ShowPopupOnTop) to opt a specific instance
    // out of any future "tap outside / back button" dismissal - PopupManager.CloseCurrentPopup()
    // already respects this, callers just need to route dismiss input through it instead of calling
    // Close() directly.
    public bool blockExternalClose;

    // public virtual, not private - a subclass with its own Awake (AlertPopup/ChangeNamePopup/
    // InvitePopup) MUST call base.Awake() or this wiring silently never happens (standard Unity
    // message-hiding gotcha, not virtual dispatch) - and C# requires an override's accessibility to
    // match the base exactly, so this has to be public since every existing override already is.
    public virtual void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    [Button]
    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public virtual void Close()
    {
        hide = true;
        onClose?.Invoke();
    }

    // Skips the fade-out for teardown paths (scene unload, forced logout) where waiting for
    // hideSpeed to bring alpha to 0 would be pointless or racy.
    public void CloseInstant()
    {
        hide = false;
        canvasGroup.alpha = 0;
        gameObject.SetActive(false);
        onClose?.Invoke();
    }

    public virtual void Show()
    {
        canvasGroup.alpha = 1;
        hide = false;
        gameObject.SetActive(true);
        onShow?.Invoke();

        AutoSelectFirstInteractable();
    }

    // Same gamepad/joystick default-selection convention as UiWindow.AutoSelectFirstInteractable -
    // see its own comment. Deferred one frame so a subclass's own Show() override (e.g.
    // InMatchSettingsPopup toggling restartButton after base.Show()) finishes first.
    protected virtual void AutoSelectFirstInteractable()
    {
        StartCoroutine(UiSelectionUtility.SelectFirstInteractableNextFrame(this));
    }

    public virtual void Update()
    {
        if (hide)
        {
            canvasGroup.alpha -= Time.unscaledDeltaTime * hideSpeed;
            if (canvasGroup.alpha <= 0)
            {
                gameObject.SetActive(false);
            }
        }
    }
    [Button]
    protected void AddPopup()
    {
        PopupManager.instance.AddPopupToQueue(this);    
    }

}