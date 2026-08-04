using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Serialization;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class RoomWindow : UiWindow, IInRoomCallbacks,IOnEventCallback
{
    public UnityEngine.UI.Button startButton; 
    public GameObject waitingToStartMessage; 
    public TMP_Text roomCode; 
    public TMP_Text regionCode; 
    public TMP_Text playerCount;
    public RoomWidget[] playerWidgets;
    public override void Show()
    {
        base.Show();
        MatchMakingConfig.Instance.Client?.AddCallbackTarget(this);
        if (MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient)
        {
            MatchMakingConfig.Instance.Client.CurrentRoom.IsVisible = false;
            MatchMakingConfig.Instance.Client.CurrentRoom.IsOpen = true;
        }
        
        UpdateUI();
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

    public override void Hide()
    {
        base.Hide();
        MatchMakingConfig.Instance.Client?.RemoveCallbackTarget(this);
    }

    public void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateUI();
        
    }

    public void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdateUI();
    }

    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
        UpdateUI();
    }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
    }

    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        UpdateUI();
    }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        UpdateUI();
    }

    public void OnMasterClientSwitched(Player newMasterClient)
    {
        UpdateUI();
    }

    public void Disconnect()
    {
        MatchMakingConfig.Instance.Client?.Disconnect();
    }
    
    [NaughtyAttributes.Button]
    private void UpdateUI() 
    {
      // Update UI controls based on if we are the master client.
      var isMasterClient = MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient;
      waitingToStartMessage.gameObject.SetActive(isMasterClient == false);
      startButton.gameObject.SetActive(isMasterClient);
      roomCode.text = MatchMakingConfig.Instance.Client.CurrentRoom.Name;
      regionCode.text = MatchMakingConfig.Instance.Client.CurrentRegion.ToUpper();


      // Update player count
      playerCount.text = string.Format("{0}/{1}",MatchMakingConfig.Instance.Client.CurrentRoom.PlayerCount,MatchMakingConfig.Instance.Client.CurrentRoom.MaxPlayers);

      foreach (var playerWidget in playerWidgets)
      {
          playerWidget.Setup("");
      }
      LogHelper.Log("RoomWindow", MatchMakingConfig.Instance.Client.CurrentRoom.Players.Count.ToString());
      int i = 0;
      foreach (var player in MatchMakingConfig.Instance.Client.CurrentRoom.Players) 
      {
          playerWidgets[i].Setup(player.Value.UserId);
          i++;
      }
    }

    public void StartClicked()
    {
        MatchMakingConfig.Instance.Client.CurrentRoom.IsVisible = true;
        GameManager.Instance.MainMenuTab.windowManager.ShowWindow<WaitingForPlayersWindow>();
        
        if (!MatchMakingConfig.Instance.Client.OpRaiseEvent((byte)PhotonMain.PhotonEventCode.WaitingForPlayers, null, new RaiseEventArgs() {Receivers = ReceiverGroup.All, CachingOption = EventCaching.AddToRoomCache}, SendOptions.SendReliable)) {
            LogHelper.Error("RoomWindow", "Failed to send start game event");
        }
    }
    
    public void OnEvent(EventData photonEvent)
    {
        switch (photonEvent.Code)
        {
            case (byte)PhotonMain.PhotonEventCode.WaitingForPlayers:
                GameManager.Instance.MainMenuTab.windowManager.ShowWindow<WaitingForPlayersWindow>();
                break;
        }
    }

    void IOnEventCallback.OnEvent(EventData photonEvent)
    {
        OnEvent(photonEvent);
    }
}
