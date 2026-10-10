using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Turns a <see cref="SettingsSliderRowWidget"/> into the Quality row: a stepped slider over
/// <see cref="RenderScaleSetting"/>, lowest quality on the left and highest on the right (so the slider value is the
/// step index counted from the other end). Hides itself on anything that isn't a phone/tablet, and keeps the slider in
/// sync every time its screen opens. Put it on the row in any settings screen - there is nothing to wire in code.
/// </summary>
[RequireComponent(typeof(SettingsSliderRowWidget))]
public class QualitySettingRowWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Show the row on desktop too (it does nothing there). Only for looking at the layout in the Editor.")]
    private bool showOnDesktop;

    private SettingsSliderRowWidget row;
    private Slider slider;
    private bool configured;

    private void Awake()
    {
        if (!RenderScaleSetting.IsAvailable && !showOnDesktop)
        {
            gameObject.SetActive(false);
            return;
        }

        row = GetComponent<SettingsSliderRowWidget>();
        slider = row.Slider;
        slider.minValue = 0;
        slider.maxValue = RenderScaleSetting.Levels.Length - 1;
        slider.wholeNumbers = true;
        string[] labels = RenderScaleSetting.Labels();
        System.Array.Reverse(labels);
        row.SetStepLabels(labels);
        slider.onValueChanged.AddListener(OnChanged);
        configured = true;
    }

    private void OnEnable()
    {
        if (!configured)
            return;

        // Without notify: syncing to the saved step must not write it straight back.
        slider.SetValueWithoutNotify(StepToSlider(RenderScaleSetting.Index));
        row.Refresh();
    }

    private void OnChanged(float value) => RenderScaleSetting.Index = StepToSlider(Mathf.RoundToInt(value));

    // Step 0 is the highest quality (100%); the slider runs the other way, so the same mapping converts both ways.
    private static int StepToSlider(int step) => RenderScaleSetting.Levels.Length - 1 - step;
}
