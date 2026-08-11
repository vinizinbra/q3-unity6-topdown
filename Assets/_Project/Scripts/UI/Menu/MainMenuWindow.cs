using System;
using System.Collections;
using System.Collections.Generic;
using Quantum;
using Quantum.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using Button = UnityEngine.UI.Button;

public class MainMenuWindow : UiWindow
{
    [FormerlySerializedAs("matchMakingConfigNew")] public MatchMakingConfig matchMakingConfig;

    [Header("Buttons")]
    public Button playButton;
    public Button quickPlayButton;
    public Button practiceButton;

    // Single button, three mutually-exclusive states (see PlayButtonClicked/Update below) -
    // replaces the old standalone reconnectButton, which used to toggle its own visibility
    // independently of whatever "Play" button already existed.
    public TMP_Text playButtonLabel;

    private void Start()
    {
        playButton.onClick.AddListener(PlayButtonClicked);
       /* quickPlayButton.onClick.AddListener(QuickPlay);
        practiceButton.onClick.AddListener(Practice);*/
    }

    private void OnDestroy()
    {
        playButton.onClick.RemoveListener(PlayButtonClicked);
       /* quickPlayButton.onClick.RemoveListener(QuickPlay);
        practiceButton.onClick.RemoveListener(Practice);*/
    }

    public override void Show()
    {
        base.Show();
        Application.targetFrameRate = 60;
    }

    void Update()
    {
        if (MatchMakingConfig.Instance.CanReconnect)
            playButtonLabel.text = "Reconnect";
        else if (PartyManager.Instance.IsPartyLeader)
            playButtonLabel.text = "Play";
        else
            playButtonLabel.text = PartyManager.Instance.IsLocalReady ? "Cancel Ready" : "Ready";
    }

    private void QuickPlay()
    {
        MatchMakingConfig.Instance.CleanReconnectConfig();
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.QUICKPLAY;
        MatchMakingConfig.Instance.Quickplay();
    }

    private void Reconnect()
    {
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.RECONNECT;
        MatchMakingConfig.Instance.ReconnectAsync();
    }

    // The single Play/Ready/Reconnect button's click handler - see the label logic in Update()
    // above for what's currently shown. Reconnect always takes priority; then either starts the
    // run (leader/solo) or just toggles this player's own ready state (non-leader party member).
    private void PlayButtonClicked()
    {
        if (MatchMakingConfig.Instance.CanReconnect)
        {
            Reconnect();
            return;
        }

        if (!PartyManager.Instance.InParty)
        {
            // Show the loading screen immediately on click, not just once StartRunner() eventually
            // runs after the room-create + StartGame event round-trip - that gap is otherwise an
            // unresponsive-looking party screen.
            GameManager.Instance.MainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
            PartyManager.Instance.QuickStartSolo();
            return;
        }

        if (PartyManager.Instance.IsPartyLeader)
        {
            if (PartyManager.Instance.AllOthersReady())
            {
                // Same reasoning as above - show it now, for the leader, rather than waiting on the
                // StartGame event to round-trip back before anything visibly happens. Every other
                // party member still gets it the moment their own client receives that event, via
                // StartRunner() itself.
                GameManager.Instance.MainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
                PartyManager.Instance.StartRun();
            }
            else
            {
                ToastManager.Instance?.Show("Waiting for everyone to be ready...");
            }
        }
        else
        {
            PartyManager.Instance.ToggleLocalReady();
        }
    }

    private void Practice()
    {
        GameManager.Instance.PlayOffline();
    }
}
