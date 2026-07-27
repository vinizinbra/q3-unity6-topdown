using System.Collections;
using System.Collections.Generic;
using Photon.Realtime;
using Quantum;
using Quantum.Demo;
using UnityEngine;
using UnityEngine.Serialization;

public class ReconnectWindow : UiWindow,IMatchmakingCallbacks
{
    private int _rejoinIterations;

    [FormerlySerializedAs("matchMakingConfig")] public MatchMakingConfigOld matchMakingConfigOld;
    public override void Show()
    {
        base.Show();
        MatchMakingConfig.Instance.Client?.AddCallbackTarget(this);
    }

    public override void Hide()
    {
        base.Hide();
        MatchMakingConfig.Instance.Client?.RemoveCallbackTarget(this);
    }

    public void OnRegionListReceived(RegionHandler regionHandler)
    {
    }

    public void OnCustomAuthenticationResponse(Dictionary<string, object> data)
    {
    }

    public void OnCustomAuthenticationFailed(string debugMessage)
    {
    }

    public void OnFriendListUpdate(List<FriendInfo> friendList)
    {
    }

    public void OnCreatedRoom()
    {
    }

    public void OnCreateRoomFailed(short returnCode, string message)
    {
    }

    public void OnJoinedRoom()
    {
        Debug.Log($"Joined or rejoined room '{MatchMakingConfig.Instance.Client.CurrentRoom.Name}' successfully as actor '{MatchMakingConfig.Instance.Client.LocalPlayer.ActorNumber}'");
        matchMakingConfigOld.IsRejoining = true;
        ((MainMenuTab)MainMenuTab.Instance).windowManager.ShowWindow<WaitingForPlayersWindow>();
    }

    public void OnJoinRoomFailed(short returnCode, string message)
    {
        
    }


    public void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("Failed to join room =>"+message);
    }

    public void OnLeftRoom()
    {
    }
}
