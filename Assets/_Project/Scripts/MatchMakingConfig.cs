using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Deterministic;
using Photon.Deterministic.Protocol;
using Photon.Realtime;
using Quantum;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;
using RuntimeConfig = Quantum.RuntimeConfig;

public class MatchMakingConfig : PgSingleton<MatchMakingConfig>, IInRoomCallbacks, IOnEventCallback, IConnectionCallbacks
{
   public RuntimeConfig RuntimeConfig;
   public RealtimeClient Client = new RealtimeClient();
   public List<RuntimePlayer> RuntimePlayers;
   public MatchmakingArguments matchmakingArguments = new MatchmakingArguments();

   public int OverwritePlayerCount;
   public TMP_InputField NameField;
   public TMP_Text ConnectionState;
   public MatchMakingType matchMakingType = MatchMakingType.QUICKPLAY;
   public static MatchMakingConfig Instance;

   public bool CanReconnect => matchmakingArguments.ReconnectInformation != null &&
                               !matchmakingArguments.ReconnectInformation.HasTimedOut;

   public enum MatchMakingType
   {
      CUSTOM,
      QUICKPLAY,
      RECONNECT
   }

   protected override void Awake()
   {
      base.Awake();
      Instance = this;
      Client.NickName = NameField.text;
      
      var appSettings = new AppSettings(PhotonServerSettings.Global.AppSettings);

      matchmakingArguments = new MatchmakingArguments 
      {
         PhotonSettings = appSettings,
         PluginName = "QuantumPlugin",
         MaxPlayers = OverwritePlayerCount > 0 ? Math.Min(OverwritePlayerCount, Quantum.Input.MAX_COUNT) : Quantum.Input.MAX_COUNT,
         UserId = Guid.NewGuid().ToString()
      };
   }

   private void OnEnable()
   {
      Client.AddCallbackTarget(this);
   }

   public void CleanReconnectConfig()
   {
      if(matchmakingArguments.ReconnectInformation != null)
         matchmakingArguments.ReconnectInformation.Timeout = DateTime.Now;
   }
   public void OnChange(string v)
   {
      Debug.LogWarning(v);
      Client.NickName = NameField.text;
   }

   public void Quickplay(string roomCode = "")
   {
      matchmakingArguments = UpdateMatchMakingRoomArguments(roomCode);

      Debug.LogWarning(matchmakingArguments.ReconnectInformation == null
         ? "Reconnect FALSE"
         : "Reconnecting with " + matchmakingArguments.ReconnectInformation.Room);
      Connect(matchmakingArguments);
   }


   MatchmakingArguments UpdateMatchMakingRoomArguments(string roomCode = "")
   {
      if (!string.IsNullOrEmpty(roomCode))
      {
         matchmakingArguments.RoomName = roomCode;
      }
      else
      {
         matchmakingArguments.RoomName = null;
      }

      return matchmakingArguments;
   }

   async void Connect(MatchmakingArguments connectionArguments) 
   {
      var mainMenuTab = GameManager.Instance.MainMenuTab;
      mainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
      await Client.ConnectToRoomAsync(connectionArguments);
      Debug.Log(Client.UserId);
   }
   public async void ReconnectAsync() 
   {
      var mainMenuTab = GameManager.Instance.MainMenuTab;
      mainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
      await Client.ReconnectToRoomAsync(matchmakingArguments);
      Debug.Log(Client.UserId);
   }
   
   async void Disconnect() {
      if (QuantumRunner.Default != null) {
         QuantumRunner.Default.Shutdown();
      }

      await Client.DisconnectAsync();
   }
   
   private void Update()
   {
      if (Client == null) return;
      
      ConnectionState.text = Client.IsConnected ? "Connected " : "Disconnected ";

      ConnectionState.text += Client.State.ToString();
      if (Client.IsConnected && Client.CurrentRoom != null)
      {
         ConnectionState.text += Client.CurrentRoom.Name;
         ConnectionState.text += Client.CurrentRoom.PlayerCount + "/" + Client.CurrentRoom.MaxPlayers;
      }
      
      Client?.Service();
   }
  

   public void StartQuantumGame()
   {
      Client.OpRaiseEvent((byte)110,1,
         new RaiseEventArgs() { Receivers = ReceiverGroup.All , CachingOption = EventCaching.AddToRoomCacheGlobal},
         SendOptions.SendReliable);
   }

   public async void StartRunner() 
   {
      var runtimeConfig = new QuantumUnityJsonSerializer().CloneConfig(RuntimeConfig);
      
      var sessionRunnerArguments = new SessionRunner.Arguments {
         RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
         GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
         ClientId = Client.UserId,
         RuntimeConfig = runtimeConfig,
         SessionConfig = QuantumDeterministicSessionConfigAsset.DefaultConfig,
         GameMode = DeterministicGameMode.Multiplayer,
         PlayerCount = OverwritePlayerCount > 0 ? Math.Min(OverwritePlayerCount, Quantum.Input.MAX_COUNT) : Quantum.Input.MAX_COUNT,
         Communicator = new QuantumNetworkCommunicator(Client)
      };

      var runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);
      for (int i = 0; i < RuntimePlayers.Count; i++) { 
         runner.Game.AddPlayer(i, RuntimePlayers[i]);
      }

      GameManager.Instance.MainMenuTab.windowManager.ShowWindow<InGameWindow>();
   }

   public void OnPlayerEnteredRoom(Player newPlayer)
   {
   }

   public void OnPlayerLeftRoom(Player otherPlayer)
   {
      Debug.LogWarning("On Player LeftRoom "+otherPlayer.NickName);
   }

   public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
   {
      //throw new NotImplementedException();
   }

   public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
   {
      //throw new NotImplementedException();
   }

   public void OnMasterClientSwitched(Player newMasterClient)
   {
      //throw new NotImplementedException();
   }
   
   public enum PhotonEventCode : byte {
      StartGame = 110,
      WaitingForPlayers = 111,
      SyncTime = 112,
   }

   public void OnEvent(EventData photonEvent)
   {
      if (photonEvent.Code == (byte)PhotonEventCode.StartGame)
      {
         if (Client.LocalPlayer.IsMasterClient)
         {
            Client.CurrentRoom.IsVisible = false;
         }
         
         StartRunner();
      }
   }

   public void OnConnected()
   {
      
   }

   public void OnConnectedToMaster()
   {
   }

   public void OnDisconnected(DisconnectCause cause)
   {
      if (cause != DisconnectCause.DisconnectByClientLogic) {
         AlertPopup.instance.Setup("Disconnected", cause.ToString(), () => 
         {
            var mainMenuTab = GameManager.Instance.MainMenuTab;
            mainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         });
      }
      else
      {
         var mainMenuTab = GameManager.Instance.MainMenuTab;
         mainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
      }
      
      if( QuantumRunner.Default != null) 
         QuantumRunner.ShutdownAll();
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
   
   public void CreateParty()
   {
      
   }
}
