using UnityEngine;
using UnityEngine.UI;

// Selectable.transition = Color Tint only recolors targetGraphic and doesn't work reliably in this
// project's setup - this recreates the same "tint on interactable state" behavior by driving an
// explicit Image directly instead, with both colors hand-assigned in the Inspector. Polls
// Selectable.interactable every frame since Selectable raises no change event of its own; only
// touches the Image when the cached value actually flips.
public class SelectableTintWidget : MonoBehaviour
{
    [SerializeField] private Selectable selectable;
    [SerializeField] private Image targetImage;
    [SerializeField] private Color interactableColor = Color.white;
    [SerializeField] private Color disabledColor = Color.gray;

    private bool? _lastInteractable;

    private void Reset()
    {
        selectable = GetComponent<Selectable>();
    }

    private void Awake()
    {
        // Color Tint is being replaced by this - leave it on None so it can't fight this over
        // targetGraphic's color.
        if (selectable != null)
            selectable.transition = Selectable.Transition.None;
    }

    private void OnEnable()
    {
        _lastInteractable = null;
    }

    private void LateUpdate()
    {
        if (selectable == null || targetImage == null)
            return;

        if (_lastInteractable != selectable.interactable)
            Apply(selectable.interactable);
    }

    private void Apply(bool interactable)
    {
        _lastInteractable = interactable;
        targetImage.color = interactable ? interactableColor : disabledColor;
    }
}
