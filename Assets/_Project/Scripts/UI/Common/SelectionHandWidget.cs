using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Visual gamepad/joystick cursor - tracks EventSystem.current.currentSelectedGameObject every
// frame and centers this Image over whichever Selectable currently has focus, so a controller
// player can see what's selected at a glance instead of relying solely on each button's own
// highlight/scale feedback (see SelectableScaleWidget). Own Canvas override (see Awake) keeps it
// drawing above every other screen-space overlay canvas regardless of where it sits in the
// hierarchy, since ChooseWindow's own PoiChoiceWindowsCanvas (sortingOrder 21) already renders
// above the root Canvas (11) this normally lives under.
[RequireComponent(typeof(RectTransform))]
public class SelectionHandWidget : MonoBehaviour
{
    [SerializeField] private Image image;
    [SerializeField] private RectTransform rectTransform;

    [SerializeField, Tooltip("Always renders above every other Screen Space - Overlay canvas in the scene, regardless of this object's own place in the hierarchy - see QuantumStats/PoiChoiceWindowsCanvas's own sortingOrder.")]
    private int overrideSortingOrder = 300;

    private Canvas _ownCanvas;

    private void Reset()
    {
        image = GetComponent<Image>();
        rectTransform = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        _ownCanvas = gameObject.AddComponent<Canvas>();
        _ownCanvas.overrideSorting = true;
        _ownCanvas.sortingOrder = overrideSortingOrder;
    }

    private void LateUpdate()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        // EventSystem doesn't always clear currentSelectedGameObject the instant a Selectable is
        // deactivated (e.g. a card's root SetActive(false) when a pick locks the others out) - a
        // stale reference to a now-inactive object would otherwise leave the hand parked on a
        // button that's no longer there.
        if (selected == null || selected.activeInHierarchy == false)
        {
            SetVisible(false);
            return;
        }

        RectTransform target = selected.GetComponent<RectTransform>();

        if (target == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        rectTransform.position = target.position;
    }

    private void SetVisible(bool visible)
    {
        if (image != null)
            image.enabled = visible;
    }
}
