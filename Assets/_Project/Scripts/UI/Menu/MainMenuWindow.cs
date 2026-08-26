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

    [Tooltip("Shown only while there is a live match to rejoin (MatchMakingConfig.CanReconnect). Hidden entirely otherwise - it is not a disabled-but-visible control.")]
    public Button reconnectButton;

    // Two mutually-exclusive states (see PlayButtonClicked/Update below). Reconnect is no longer
    // one of them - it has its own button now, so Play never silently turns into something else.
    public TMP_Text playButtonLabel;

    [Tooltip("Optional - the party panel that owns the room-code field. While a code is typed there and this player isn't already in a party, the Play button becomes Join and joins that room instead of starting a run. Left unassigned, Play behaves exactly as before.")]
    public PartyRoomWidget partyRoom;

    private void Start()
    {
        playButton.onClick.AddListener(PlayButtonClicked);

        if (reconnectButton != null)
            reconnectButton.onClick.AddListener(Reconnect);
       /* quickPlayButton.onClick.AddListener(QuickPlay);
        practiceButton.onClick.AddListener(Practice);*/
    }

    private void OnDestroy()
    {
        playButton.onClick.RemoveListener(PlayButtonClicked);

        if (reconnectButton != null)
            reconnectButton.onClick.RemoveListener(Reconnect);
       /* quickPlayButton.onClick.RemoveListener(QuickPlay);
        practiceButton.onClick.RemoveListener(Practice);*/
    }

    public override void Show()
    {
        base.Show();
        Application.targetFrameRate = 120;
    }

    void Update()
    {
        // CanReconnect is already the full "is there anything to rejoin" answer - not in a room,
        // reconnect information present, and not timed out. See MatchMakingConfig.
        if (reconnectButton != null)
        {
            bool canReconnect = MatchMakingConfig.Instance.CanReconnect;
            if (reconnectButton.gameObject.activeSelf != canReconnect)
                reconnectButton.gameObject.SetActive(canReconnect);
        }

        if (ShouldOfferJoin)
            playButtonLabel.text = "Join";
        else if (PartyManager.Instance.IsPartyLeader)
            playButtonLabel.text = "Play";
        else
            playButtonLabel.text = PartyManager.Instance.IsLocalReady ? "Cancel Ready" : "Ready";
    }

    // A room code typed but not yet acted on means the player is clearly trying to join someone,
    // so the single prominent button should do that rather than drop them into a solo run they'd
    // have to back out of. Gated on not already being in a party: once in one, the code field is
    // behind the room panel and Play has to go back to being Play/Ready.
    private bool ShouldOfferJoin => partyRoom != null
        && partyRoom.HasPendingRoomCode
        && PartyManager.Instance.InParty == false;

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

    // The Play/Ready button's click handler - see the label logic in Update() above for what's
    // currently shown. Either starts the run (leader/solo) or just toggles this player's own ready
    // state (non-leader party member). Reconnect is reconnectButton's job, not this one's.
    private void PlayButtonClicked()
    {
        // Checked before the solo path below, since that path is exactly what this replaces.
        if (ShouldOfferJoin)
        {
            PartyManager.Instance.JoinParty(partyRoom.PendingRoomCode);
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
