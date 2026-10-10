using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Turns a settings row's Toggle into the Haptics on/off switch (<see cref="Haptics.Enabled"/>, persisted). Syncs to
/// the saved value every time its screen opens. Put it on the row in any settings screen - nothing to wire in code.
/// </summary>
public class HapticsSettingRowWidget : MonoBehaviour
{
    [SerializeField] private Toggle toggle;
    [SerializeField, Tooltip("Optional ON/OFF readout, matching the value column of the slider rows.")]
    private TMP_Text valueText;

    private void Awake()
    {
        if (toggle == null)
            toggle = GetComponentInChildren<Toggle>(true);

        toggle.onValueChanged.AddListener(OnChanged);
    }

    // Without notify: syncing to the saved value must not write it straight back.
    private void OnEnable()
    {
        toggle.SetIsOnWithoutNotify(Haptics.Enabled);
        RefreshValue();
    }

    private void OnChanged(bool value)
    {
        Haptics.Enabled = value;
        RefreshValue();

        // Turning it on is the one moment worth confirming it works.
        if (value)
            Haptics.Play(HapticPreset.Success);
    }

    private void RefreshValue()
    {
        if (valueText != null)
            valueText.text = toggle.isOn ? "ON" : "OFF";
    }
}
