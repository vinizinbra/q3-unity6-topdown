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
            case MatchMakingConfig.MatchMakingType.CUSTOM:
                mainMenuTab.windowManager.ShowWindow<RoomWindow>();
                break;
            case MatchMakingConfig.MatchMakingType.QUICKPLAY:
                mainMenuTab.windowManager.ShowWindow<WaitingForPlayersWindow>();
                break;
            case MatchMakingConfig.MatchMakingType.RECONNECT:
                break;
            default:
                break;
        }
    }

    public void OnCreateRoomFailed(short returnCode, string message) {
        AlertPopup.instance.Setup("Error", $"Create room failed [{returnCode}]: '{message}'", () => MatchMakingConfig.Instance.Client?.Disconnect());
    }

    public void OnJoinedRoom()
    {
        MainMenuTab mainMenuTab = GameManager.Instance.MainMenuTab;
        switch (MatchMakingConfig.Instance.matchMakingType)
        {
            case MatchMakingConfig.MatchMakingType.CUSTOM:
                mainMenuTab.windowManager.ShowWindow<RoomWindow>();
                break;
            case MatchMakingConfig.MatchMakingType.QUICKPLAY:
                mainMenuTab.windowManager.ShowWindow<WaitingForPlayersWindow>();
                break;
            case MatchMakingConfig.MatchMakingType.RECONNECT:
                break;
        }
    }

    public void OnJoinRoomFailed(short returnCode, string message) 
    {
        AlertPopup.instance.Setup("Error", $"Joining room failed [{returnCode}]: '{message}'", () => MatchMakingConfig.Instance.Client?.Disconnect());
    }

    public void OnJoinRandomFailed(short returnCode, string message) 
    {
        if (returnCode == ErrorCode.NoRandomMatchFound) 
        {
            /*
            if (!MatchMakingConfig.Instance.Client.OpCreateRoom(matchMakingConfigOld.enterRoomParams)) {
                AlertPopup.instance.Setup("Error", "Failed to send join or create room operation", () => MatchMakingConfig.Instance.Client?.Disconnect());
            }*/
        }
        else {
            AlertPopup.instance.Setup("Error", $"Join random failed [{returnCode}]: '{message}'", () => MatchMakingConfig.Instance.Client?.Disconnect());
        }
    }

    public void OnLeftRoom() {
        AlertPopup.instance.Setup("Error", "Left the room unexpectedly", () => MatchMakingConfig.Instance.Client?.Disconnect());
    }
}
