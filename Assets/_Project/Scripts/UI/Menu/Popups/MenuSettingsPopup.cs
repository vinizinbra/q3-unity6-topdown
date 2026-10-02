using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MenuScene settings popup, opened by the main menu's SettingsButton (wired to Open). The three
// volume sliders drive the same persisted AudioManager category volumes as InMatchSettingsPopup, so
// the two popups always agree and there is nothing to save here.
//
// The region dropdown is option 0 = "None" (auto) followed by PhotonRegionSettings.Regions. Under
// None the next connect finds the best region and saves it as the selection - see
// PhotonRegionSettings - which is why the dropdown refreshes on PhotonRegionSettings.Changed.
// A change only applies to the NEXT connect, so the dropdown is locked while already in a room
// (a party) rather than silently doing nothing until the player leaves.
public class MenuSettingsPopup : UiPopup
{
    private const string NoneLabel = "None (find best)";

    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider voiceSlider;
    [SerializeField] private TMP_Dropdown regionDropdown;
    [SerializeField, Tooltip("Optional one-line hint under the dropdown (what None does / why it is locked).")]
    private TMP_Text regionHintText;

    protected override bool CloseOnDimClickByDefault => true;

    public override void Awake()
    {
        base.Awake();

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicChanged);

        if (voiceSlider != null)
            voiceSlider.onValueChanged.AddListener(OnVoiceChanged);

        if (regionDropdown != null)
        {
            var options = new List<TMP_Dropdown.OptionData> { new(NoneLabel) };
            foreach (var region in PhotonRegionSettings.Regions)
                options.Add(new TMP_Dropdown.OptionData(region.DisplayName));

            regionDropdown.ClearOptions();
            regionDropdown.AddOptions(options);
            regionDropdown.onValueChanged.AddListener(OnRegionChanged);
        }
    }

    private void OnEnable() => PhotonRegionSettings.Changed += RefreshRegion;

    private void OnDisable() => PhotonRegionSettings.Changed -= RefreshRegion;

    // Wired to the main menu's SettingsButton.
    public void Open()
    {
        if (PopupManager.instance != null)
            PopupManager.instance.AddPopupToQueue(this);
    }

    public override void Show()
    {
        base.Show();

        // Without notify: syncing the handles to the saved values must not write them straight back.
        if (sfxSlider != null)
            sfxSlider.SetValueWithoutNotify(AudioManager.SfxVolume);

        if (musicSlider != null)
            musicSlider.SetValueWithoutNotify(AudioManager.MusicVolume);

        if (voiceSlider != null)
            voiceSlider.SetValueWithoutNotify(AudioManager.VoiceVolume);

        RefreshRegion();
    }

    private void RefreshRegion()
    {
        if (regionDropdown == null)
            return;

        regionDropdown.SetValueWithoutNotify(RegionToIndex(PhotonRegionSettings.SelectedRegion));
        regionDropdown.RefreshShownValue();

        bool inRoom = MatchMakingConfig.Instance != null && MatchMakingConfig.Instance.Client != null && MatchMakingConfig.Instance.Client.InRoom;
        regionDropdown.interactable = !inRoom;

        if (regionHintText != null)
        {
            regionHintText.text = inRoom
                ? "Leave the party to change region."
                : PhotonRegionSettings.IsAuto
                    ? "The best region will be found and saved on your next connect."
                    : "Friends must use the same region to join your party.";
        }
    }

    private void OnRegionChanged(int index)
    {
        PhotonRegionSettings.SelectedRegion = index <= 0 ? string.Empty : PhotonRegionSettings.Regions[index - 1].Code;
        RefreshRegion();
    }

    private static int RegionToIndex(string code)
    {
        if (string.IsNullOrEmpty(code))
            return 0;

        for (var i = 0; i < PhotonRegionSettings.Regions.Length; i++)
        {
            if (PhotonRegionSettings.Regions[i].Code == code)
                return i + 1;
        }

        // A saved code outside the list (a region Photon added later) - show None rather than a
        // wrong region; the saved value itself is left untouched.
        return 0;
    }

    private static void OnSfxChanged(float value) => AudioManager.SfxVolume = value;

    private static void OnMusicChanged(float value) => AudioManager.MusicVolume = value;

    private static void OnVoiceChanged(float value) => AudioManager.VoiceVolume = value;
}
