using UnityEngine;
using UnityEngine.UI;

// Pause-menu style settings popup, opened mid-match through InMatchPopupManager (Escape, or any
// button wired to InMatchPopupManager.OpenSettings). Music/SFX sliders drive AudioManager's two
// persisted category volumes - see AudioManager.MusicVolume - so there is nothing to save here.
//
// Disconnect goes through MatchMakingConfig.LeaveMatch, which already routes offline vs online (see
// its own comment). Restart only makes sense for a purely local session - an online run is a shared
// deterministic simulation that one client can't restart out from under the others - so the button
// is hidden unless GameManager.isPlayingOffline, re-evaluated on every Show.
public class InMatchSettingsPopup : UiPopup
{
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Button disconnectButton;
    [SerializeField, Tooltip("Hidden unless the match is being played offline.")]
    private Button restartButton;

    // The only popup a dim-background click (or Escape/Start) is allowed to dismiss.
    protected override bool CloseOnDimClickByDefault => true;

    public override void Awake()
    {
        base.Awake();

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicChanged);

        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectClicked);

        if (restartButton != null)
            restartButton.onClick.AddListener(OnRestartClicked);
    }

    public override void Show()
    {
        base.Show();

        // Without notify: syncing the handles to the saved values must not write them straight back.
        if (sfxSlider != null)
            sfxSlider.SetValueWithoutNotify(AudioManager.SfxVolume);

        if (musicSlider != null)
            musicSlider.SetValueWithoutNotify(AudioManager.MusicVolume);

        if (restartButton != null)
            restartButton.gameObject.SetActive(GameManager.Instance != null && GameManager.Instance.isPlayingOffline);
    }

    private static void OnSfxChanged(float value) => AudioManager.SfxVolume = value;

    private static void OnMusicChanged(float value) => AudioManager.MusicVolume = value;

    private void OnDisconnectClicked()
    {
        // Instant, not the fade: the scene (and this popup with it) is about to be torn down.
        CloseInstant();
        MatchMakingConfig.Instance.LeaveMatch();
    }

    private void OnRestartClicked()
    {
        CloseInstant();
        MatchMakingConfig.Instance.RestartOfflineMatch();
    }
}
