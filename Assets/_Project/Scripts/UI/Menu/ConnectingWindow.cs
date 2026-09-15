using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Realtime;
using Quantum;
using Quantum.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class ConnectingWindow : UiWindow,IMatchmakingCallbacks
{
    public TMP_Text connectingText;
    public override void Show()
    {
        base.Show();
        // Defensive dedup: if Show() is ever called twice without an intervening Hide() (two
        // overlapping reconnect attempts, for instance), AddCallbackTarget would register this
        // instance twice - a single later Hide()/RemoveCallbackTarget only strips ONE of those
        // registrations, leaving this window permanently subscribed to OnLeftRoom (and everything
        // else) for the rest of the session, silently misreporting any later, unrelated room leave
        // as "Left the room unexpectedly". Removing first makes this idempotent no matter how many
        // times Show() fires without a matching Hide().
        MatchMakingConfig.Instance.Client.RemoveCallbackTarget(this);
        MatchMakingConfig.Instance.Client.AddCallbackTarget(this);
    }

    public override void Hide()
    {
        base.Hide();
        MatchMakingConfig.Instance.Client?.RemoveCallbackTarget(this);
    }
    
    public void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        
    }

    private void Update()
    {
        connectingText.text = MatchMakingConfig.Instance.Client.State.ToString();
    }

    public void OnCreatedRoom()
    {
        MainMenuTab mainMenuTab = GameManager.Instance.MainMenuTab;

        switch (MatchMakingConfig.Instance.matchMakingType)
        {
            case MatchMakingConfig.MatchMakingType.QUICKPLAY:
                mainMenuTab.windowManager.ShowWindow<WaitingForPlayersWindow>();
                break;
            case MatchMakingConfig.MatchMakingType.RECONNECT:
                // Intentionally nothing: MatchMakingConfig.ReconnectAsync owns what happens next
                // once the rejoin resolves (StartRunner for a live match, MainMenuWindow for a
                // party room that never started). This callback fires mid-await, before either.
                break;
            default:
                break;
        }
    }

    public void OnCreateRoomFailed(short returnCode, string message) {
        AlertPopup.Show("Error", $"Create room failed [{returnCode}]: '{message}'", () => MatchMakingConfig.Instance.Client?.Disconnect());
    }

    public void OnJoinedRoom()
    {
        MainMenuTab mainMenuTab = GameManager.Instance.MainMenuTab;
        switch (MatchMakingConfig.Instance.matchMakingType)
        {
            case MatchMakingConfig.MatchMakingType.QUICKPLAY:
                mainMenuTab.windowManager.ShowWindow<WaitingForPlayersWindow>();
                break;
            case MatchMakingConfig.MatchMakingType.RECONNECT:
                // See OnCreatedRoom above - ReconnectAsync drives the transition, not this.
                break;
        }
    }

    public void OnJoinRoomFailed(short returnCode, string message)
    {
        AlertPopup.Show("Error", $"Joining room failed [{returnCode}]: '{message}'", () => MatchMakingConfig.Instance.Client?.Disconnect());
    }

    public void OnJoinRandomFailed(short returnCode, string message)
    {
        if (returnCode == ErrorCode.NoRandomMatchFound)
        {
        }
        else {
            AlertPopup.Show("Error", $"Join random failed [{returnCode}]: '{message}'", () => MatchMakingConfig.Instance.Client?.Disconnect());
        }
    }

    public void OnLeftRoom() {
        AlertPopup.Show("Error", "Left the room unexpectedly", () => MatchMakingConfig.Instance.Client?.Disconnect());
    }
}
