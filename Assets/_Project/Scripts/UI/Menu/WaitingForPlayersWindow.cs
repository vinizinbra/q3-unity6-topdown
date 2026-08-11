using System.Collections.Generic;
using NaughtyAttributes;
using Photon.Client;
using Photon.Realtime;
using Quantum;
using Quantum.Demo;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class WaitingForPlayersWindow : UiWindow, IInRoomCallbacks,IOnEventCallback
{
    public TMP_Text playerCount;
    public TMP_Text timeText;
    public float startTime = 5;
    private float currentTime = 5;
    public  GameObject startButton;
    public override void Show()
    {
        MatchMakingConfig.Instance.Client?.AddCallbackTarget(this);
        base.Show();
        currentTime = startTime;
        
        object mapGuidValue = null;
        UpdateUI();

        if (MatchMakingConfig.Instance.Client != null &&
            MatchMakingConfig.Instance.Client.CurrentRoom.CustomProperties.TryGetValue("MAP-GUID", out mapGuidValue) &&
            MatchMakingConfig.Instance.Client.CurrentRoom.CustomProperties.TryGetValue("STARTED",  out var started)) 
        {
            // The game is already running as indicated by the room property. Run the start game procedure.
            LogHelper.Log("WaitingForPlayers", "Game already running");
            var mapGuid = (AssetGuid)(long)mapGuidValue;
            MatchMakingConfig.Instance.StartQuantumGame();

            ((MainMenuTab)MainMenuTab.Instance).windowManager.ShowWindow<InMatchWindow>();
            LogHelper.Log("WaitingForPlayers", "Ingamewindow");
        }
        
    }

    public override void Hide()
    {
        base.Hide();
        MatchMakingConfig.Instance.Client?.RemoveCallbackTarget(this);
    }
    public void Disconnect()
    {
        MatchMakingConfig.Instance.Client?.Disconnect();
    }
    public void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient)
        {
            PhotonHashtable hash = new PhotonHashtable();
            hash["CurrentTime"] = currentTime;
            MatchMakingConfig.Instance.Client.OpRaiseEvent((byte)PhotonMain.PhotonEventCode.SyncTime, hash, new RaiseEventArgs() { Receivers = ReceiverGroup.All , TargetActors = new int[]{newPlayer.ActorNumber}}, SendOptions.SendReliable);
        }
      
        UpdateUI();
    }

    public void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdateUI();
    }

    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
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
    
    [Button]
    private void UpdateUI() {
      if (MatchMakingConfig.Instance.Client == null || MatchMakingConfig.Instance.Client.InRoom == false) {
          MatchMakingConfig.Instance.Client?.Disconnect();
        return;
      }

      playerCount.text = string.Format("{0}/{1}",MatchMakingConfig.Instance.Client.CurrentRoom.PlayerCount,MatchMakingConfig.Instance.Client.CurrentRoom.MaxPlayers);

    }

    private void Update()
    {
        var isMasterClient = MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient;
        startButton.SetActive(isMasterClient);
        
        if (isMasterClient)
        {
            currentTime -= Time.deltaTime;
            
            timeText.text = currentTime.ToString("0")+"s";
            if (currentTime <= 0)
            {
            }
            
        }
    }

    [Button]
    public void StartGame()
    {
        GameManager.Instance.MainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
        
        if (MatchMakingConfig.Instance.Client != null && MatchMakingConfig.Instance.Client.InRoom && MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient && MatchMakingConfig.Instance.Client.CurrentRoom.IsOpen) 
        {
            if (!MatchMakingConfig.Instance.Client.OpRaiseEvent((byte)MatchMakingConfig.PhotonEventCode.StartGame, null, new RaiseEventArgs() {Receivers = ReceiverGroup.All,CachingOption = EventCaching.AddToRoomCacheGlobal}, SendOptions.SendReliable)) {
                LogHelper.Error("WaitingForPlayers", "Failed to send start game event");
            }
        }
    }
    
    public void OnEvent(EventData photonEvent) {
      switch (photonEvent.Code) 
      {
          case (byte)PhotonMain.PhotonEventCode.SyncTime:
              //currentTime = (int) photonEvent.["CurrentTime"];
              break;

          case (byte)MatchMakingConfig.PhotonEventCode.StartGame:
              /*
              var mainMenuTab = GameManager.Instance.SelectTab<MainMenuTab>() as MainMenuTab;
              mainMenuTab.windowManager.ShowWindow<InMatchWindow>();
              Debug.LogWarning("Starting Game Event");
              MatchMakingConfig.Instance.Client.CurrentRoom.CustomProperties.TryGetValue("MAP-GUID", out object mapGuidValue);
              if (mapGuidValue == null) {
                return;
              }

              if (MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient) 
              {
                var ht = new PhotonHashtable() {{"STARTED", true}};
                MatchMakingConfig.Instance.Client.CurrentRoom.IsVisible = false;
                MatchMakingConfig.Instance.Client.CurrentRoom.IsOpen = false;
                MatchMakingConfig.Instance.Client.CurrentRoom.SetCustomProperties(ht);

                if (MatchMakingConfig.Instance.Client.CurrentRoom.CustomProperties.TryGetValue("HIDE-ROOM", out var hideRoom) && (bool)hideRoom) {
                    MatchMakingConfig.Instance.Client.CurrentRoom.IsVisible = false;
                }
              }
            Debug.Log("Start quantum game and load ingame window");
            MatchMakingConfig.Instance.StartQuantumGame();
            */
           

          break;
      }
    }
}
