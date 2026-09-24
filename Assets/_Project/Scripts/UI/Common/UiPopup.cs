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

    // Opt-in: popups close only through their own buttons unless this is ticked (or a subclass
    // overrides CloseOnDimClickByDefault, like InMatchSettingsPopup). PopupManager.CloseCurrentPopup()
    // - the dim/backdrop click and back button - respects CanCloseOnDimClick, so route dismiss input
    // through it instead of calling Close() directly.
    [Tooltip("If ticked, clicking the dim background (or back button) closes this popup.")]
    [SerializeField] private bool closeOnDimClick;

    protected virtual bool CloseOnDimClickByDefault => closeOnDimClick;

    // Per-show override set by whoever opens the popup (ShowPopupOnTop / Open<T> / AddPopupToQueue
    // with an explicit value); null = use the default above. Cleared on close so it never leaks
    // into the next time this reused instance is shown.
    private bool? closeOnDimClickOverride;

    public bool CanCloseOnDimClick => closeOnDimClickOverride ?? CloseOnDimClickByDefault;

    public void SetCloseOnDimClickOverride(bool? value) => closeOnDimClickOverride = value;

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
        closeOnDimClickOverride = null;
        onClose?.Invoke();
    }

    // Skips the fade-out for teardown paths (scene unload, forced logout) where waiting for
    // hideSpeed to bring alpha to 0 would be pointless or racy.
    public void CloseInstant()
    {
        hide = false;
        canvasGroup.alpha = 0;
        gameObject.SetActive(false);
        closeOnDimClickOverride = null;
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