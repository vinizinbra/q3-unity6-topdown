using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// A TMP_InputField made for gamepad/keyboard navigation: it takes focus like any other element but only starts
/// editing when Submit is pressed on it (or on a mouse/touch click). Two things in stock TMP get in the way:
///  - it starts editing the moment it is selected, then swallows every move/key event, so just navigating onto it
///    trapped the stick inside the text field (<see cref="TMP_InputField.shouldActivateOnSelect"/> is turned off here);
///  - its Cancel handler ACTIVATES an unfocused field (OnCancel sets "activate next update"), so pressing Cancel
///    (B) on the field started typing instead of going back - ignored here unless the field is being edited.
/// While editing, Enter ends it (TMP) and Cancel ends it too. MenuNavigationController holds off Back / L1 / R1 meanwhile.
/// </summary>
public class SubmitToEditInputField : TMP_InputField
{
    protected override void Awake()
    {
        base.Awake();
        shouldActivateOnSelect = false;
    }

    public override void OnCancel(BaseEventData eventData)
    {
        if (!isFocused)
            return;

        base.OnCancel(eventData);
    }

    /// <summary>True while the selected object is a text field being edited.</summary>
    public static bool IsEditing(GameObject selected)
    {
        if (selected == null)
            return false;

        TMP_InputField input = selected.GetComponent<TMP_InputField>();
        return input != null && input.isFocused;
    }
}
