using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Keeps a Canvas that gets switched off (Canvas.enabled = false) from still taking input. Disabling a
/// Canvas only stops it from drawing: its Selectables stay in the EventSystem's navigation list, so a
/// gamepad stick (or Submit) could still move to and press invisible buttons. This mirrors the Canvas's
/// enabled state onto a CanvasGroup (interactable / blocksRaycasts) and its GraphicRaycaster, and drops
/// the current selection if it sits under this Canvas - whoever toggles Canvas.enabled needs no changes.
/// Used on the MenuScene root Canvas, which stays loaded underneath the gameplay HUD for the whole session.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class CanvasInputGateWidget : MonoBehaviour
{
    private Canvas canvas;
    private CanvasGroup group;
    private GraphicRaycaster raycaster;
    private bool? applied;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        group = GetComponent<CanvasGroup>();
        if (group == null)
            group = gameObject.AddComponent<CanvasGroup>();

        raycaster = GetComponent<GraphicRaycaster>();
    }

    private void Update()
    {
        if (applied == canvas.enabled)
            return;

        Apply(canvas.enabled);
    }

    private void Apply(bool open)
    {
        applied = open;
        group.interactable = open;
        group.blocksRaycasts = open;
        if (raycaster != null)
            raycaster.enabled = open;

        if (open || EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.transform.IsChildOf(transform))
            EventSystem.current.SetSelectedGameObject(null);
    }
}
