using UnityEngine;
using UnityEngine.UI;

// Sits on DirectorTimelineUiWidget's markerPrefab - explicit serialized reference to the phase icon
// Image, instead of GetComponentInChildren<Image> guessing which Image under the prefab is the icon
// (the marker's own tick/line visual is often an Image too, so a blind search could grab that one).
public class DirectorPhaseMarkerWidget : MonoBehaviour
{
    [SerializeField] private Image icon;

    public Image Icon => icon;
}
