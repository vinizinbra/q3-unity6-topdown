using UnityEngine;

// Draws a "+" button next to a SoundData reference field that opens a searchable multi-select
// picker over every AudioClip in the project (SoundClipPickerWindow) instead of the usual
// "right-click > Create > Audio > ... > drag clips in one at a time" dance. The new SoundData is
// authored with every ticked clip as its `variants` (RandomNoRepeat pick, so more than one clip is
// an instant randomized variation set) and saved under Folder, created if missing.
//
// Complements SoundDataCreator (the Project-window "select clips first" workflow) for the opposite
// direction: start from the FIELD that needs a sound and search for clips, rather than starting
// from clips already selected in the Project window.
public class SoundDataPickerAttribute : PropertyAttribute
{
    public readonly string Folder;

    public SoundDataPickerAttribute(string folder = "Assets/_Project/Audio/Generated")
    {
        Folder = folder;
    }
}
