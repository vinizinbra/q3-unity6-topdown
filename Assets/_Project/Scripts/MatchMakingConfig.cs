using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Client;
using Photon.Deterministic;
using Photon.Deterministic.Protocol;
using Photon.Realtime;
using Playtime.Core;
using Quantum;
using QuantumUser.View.Util;
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

   [Tooltip("Manually flip this on right before cutting a real release build. Not a Development-Build check (playtest builds here aren't consistently built as Development Builds) - an explicit, hand-set flag so checksum verification stays on by default (including in playtest builds) and is only turned off deliberately. See SessionConfig.asset's ChecksumInterval - Quantum's own guidance is 'useful during development, set to zero for release'.")]
   public bool DisableChecksumsForRelease;

   // Player's own meta-progression weapon-talent level, carried in from outside this match (e.g.
   // an account/profile screen elsewhere would be what actually raises this over time) - read here
   // right before AddPlayer and copied onto RuntimePlayer.WeaponLevel, which
   // PlayerSpawnUtility.Spawn seeds CharacterStats.WeaponTalentLevel from once at spawn. See that
   // field's own comment.
   private static readonly PlayerPrefInt WeaponTalentLevelPref = new PlayerPrefInt("weapon_talent_level", 0);

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

      if (NameField == null)
      {
         LogHelper.Error("MatchMaking", "Awake: NameField is not assigned.");
      }
      Client.NickName = NameField != null ? NameField.text : string.Empty;

      var globalSettings = PhotonServerSettings.Global;

      matchmakingArguments = new MatchmakingArguments
      {
         PhotonSettings = BuildAppSettings(),
         PluginName = "QuantumPlugin",
         MaxPlayers = OverwritePlayerCount > 0 ? Math.Min(OverwritePlayerCount, Quantum.Input.MAX_COUNT) : Quantum.Input.MAX_COUNT,
         UserId = Guid.NewGuid().ToString(),
         // Must be non-null before Connect for ConnectToRoomAsync to populate/persist it on a successful join (see MatchmakingExtensions.ConnectToRoomAsync).
         ReconnectInformation = QuantumReconnectInformation.Load(),
         // Without these the room is destroyed the instant a player disconnects (both default to 0), so there is nothing left to reconnect to.
         PlayerTtlInSeconds = globalSettings != null ? globalSettings.PlayerTtlInSeconds : 0,
         EmptyRoomTtlInSeconds = globalSettings != null ? globalSettings.EmptyRoomTtlInSeconds : 0
      };

      Instance = this;
   }

   private AppSettings BuildAppSettings()
   {
      try
      {
         var globalSettings = PhotonServerSettings.Global;
         if (globalSettings == null || globalSettings.AppSettings == null)
         {
            LogHelper.Error("MatchMaking", "PhotonServerSettings.Global.AppSettings is null.");
            return null;
         }
         return new AppSettings(globalSettings.AppSettings);
      }
      catch (Exception e)
      {
         LogHelper.Error("MatchMaking", $"Failed to load PhotonServerSettings.Global: {e}");
         return null;
      }
   }

   private void OnEnable()
   {
      Client.AddCallbackTarget(this);
   }

   public void CleanReconnectConfig()
   {
      if(matchmakingArguments.ReconnectInformation != null)
         matchmakingArguments.ReconnectInformation.Timeout = DateTime.Now;
      QuantumReconnectInformation.Reset();
   }
   public void OnChange(string v)
   {
      LogHelper.Warn("MatchMaking", v);
      Client.NickName = NameField.text;
   }

   public void Quickplay(string roomCode = "")
   {
      if (matchmakingArguments.PhotonSettings == null)
      {
         LogHelper.Warn("MatchMaking", "Quickplay: PhotonSettings was never set during Awake, rebuilding now.");
         matchmakingArguments.PhotonSettings = BuildAppSettings();
      }

      if (matchmakingArguments.PhotonSettings == null)
      {
         HandleConnectFailure(new Exception("Photon settings could not be loaded (PhotonServerSettings.Global unavailable)."));
         return;
      }

      matchmakingArguments = UpdateMatchMakingRoomArguments(roomCode);

      LogHelper.Warn("MatchMaking", matchmakingArguments.ReconnectInformation == null
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
      try
      {
         await Client.ConnectToRoomAsync(connectionArguments);
         LogHelper.Log("MatchMaking", Client.UserId);
      }
      catch (Exception e)
      {
         HandleConnectFailure(e);
      }
   }
   public async void ReconnectAsync()
   {
      var mainMenuTab = GameManager.Instance.MainMenuTab;
      mainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
      try
      {
         await Client.ReconnectToRoomAsync(matchmakingArguments);
         LogHelper.Log("MatchMaking", Client.UserId);
      }
      catch (Exception e)
      {
         HandleConnectFailure(e);
      }
   }

   private void HandleConnectFailure(Exception e)
   {
      LogHelper.Error("MatchMaking", $"Connect failed: {e}");
      AlertPopup.instance.Setup("Connection Failed", e.Message, () =>
      {
         GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
      });
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

   // Name of the additively-loaded gameplay scene (see QuantumMap.asset's Scene field). Quantum's
   // own SDK code (QuantumCallbackHandler_UnityCallbacks in QuantumUnityRuntime.cs) unloads the
   // previous match's copy asynchronously over several frames on a DontDestroyOnLoad host, with no
   // memory of that cleanup carried into the next match's own tracker instance. Starting a new
   // match before that unload finishes leaves two copies of the scene loaded at once - two Main
   // Cameras, two AudioListeners. WaitForPreviousGameplaySceneToUnloadAsync below guards against
   // that race instead of relying on timing to always favor us.
   private const string GameplaySceneName = "QuantumGameScene";
   private const float GameplaySceneUnloadTimeoutSeconds = 5f;

   private async Task WaitForPreviousGameplaySceneToUnloadAsync()
   {
      if (!SceneManager.GetSceneByName(GameplaySceneName).IsValid())
         return;

      LogHelper.Warn("MatchMaking", $"{GameplaySceneName} is still loaded from a previous match - waiting for it to unload before starting a new session.");

      float startTime = Time.realtimeSinceStartup;
      while (SceneManager.GetSceneByName(GameplaySceneName).IsValid())
      {
         if (Time.realtimeSinceStartup - startTime > GameplaySceneUnloadTimeoutSeconds)
         {
            LogHelper.Error("MatchMaking", $"{GameplaySceneName} did not unload within {GameplaySceneUnloadTimeoutSeconds}s - starting new session anyway.");
            return;
         }
         await Task.Yield();
      }
   }

   public async void StartRunner()
   {
      await WaitForPreviousGameplaySceneToUnloadAsync();

      var runtimeConfig = new QuantumUnityJsonSerializer().CloneConfig(RuntimeConfig);

      var sessionConfig = QuantumDeterministicSessionConfigAsset.DefaultConfig;
      if (DisableChecksumsForRelease)
         sessionConfig.ChecksumInterval = 0;

      var sessionRunnerArguments = new SessionRunner.Arguments {
         RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
         GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
         ClientId = Client.UserId,
         RuntimeConfig = runtimeConfig,
         SessionConfig = sessionConfig,
         GameMode = DeterministicGameMode.Multiplayer,
         PlayerCount = OverwritePlayerCount > 0 ? Math.Min(OverwritePlayerCount, Quantum.Input.MAX_COUNT) : Quantum.Input.MAX_COUNT,
         Communicator = new QuantumNetworkCommunicator(Client)
      };

      var runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);

      // Clamp - PlayerPrefInt stores a plain int, RuntimePlayer.WeaponLevel is a byte.
      byte weaponTalentLevel = (byte)Mathf.Clamp(WeaponTalentLevelPref.Value, 0, byte.MaxValue);

      for (int i = 0; i < RuntimePlayers.Count; i++) {
         RuntimePlayers[i].WeaponLevel = weaponTalentLevel;
         runner.Game.AddPlayer(i, RuntimePlayers[i]);
      }

      GameManager.Instance.MainMenuTab.windowManager.ShowWindow<InGameWindow>();
   }

   public void OnPlayerEnteredRoom(Player newPlayer)
   {
   }

   public void OnPlayerLeftRoom(Player otherPlayer)
   {
      LogHelper.Warn("MatchMaking", "On Player LeftRoom " + otherPlayer.NickName);
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
