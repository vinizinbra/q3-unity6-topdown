using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A labelled slider row of a settings screen: keeps the value readout next to the slider in sync, as a percentage
/// or - when step labels are given - as the label of the current step ("80%", "Low"...). Polls the slider, so it
/// also follows SetValueWithoutNotify (which fires no event) - popups can keep syncing sliders as they always did.
/// </summary>
public class SettingsSliderRowWidget : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text valueText;
    [SerializeField, Tooltip("Optional: one label per step, from the slider's minimum. Empty = show the value as a percentage.")]
    private string[] stepLabels;

    private float last = float.NaN;

    public Slider Slider => slider;

    private void OnEnable() => Refresh();

    private void Update()
    {
        if (slider.value != last)
            Refresh();
    }

    public void SetStepLabels(string[] labels)
    {
        stepLabels = labels;
        Refresh();
    }

    public void Refresh()
    {
        last = slider.value;

        if (valueText == null)
            return;

        if (stepLabels != null && stepLabels.Length > 0)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(slider.value - slider.minValue), 0, stepLabels.Length - 1);
            valueText.text = stepLabels[step];
        }
        else
        {
            valueText.text = Mathf.RoundToInt(slider.normalizedValue * 100f) + "%";
        }
    }
}
