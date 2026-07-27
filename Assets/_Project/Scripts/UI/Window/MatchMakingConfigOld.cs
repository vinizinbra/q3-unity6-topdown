using System;
using Photon.Deterministic;
using Photon.Realtime;
using Playtime.Core;
using Quantum;
using Quantum.Demo;
using UnityEngine;

public class MatchMakingConfigOld : MonoBehaviour
{
    public enum MatchMakingType
    {
        CUSTOM,
        QUICKPLAY,
        RECONNECT
    }

    public MatchMakingType currentMatchMakingType;
    public TMPro.TMP_InputField sizeInputfield;
    public RuntimeConfig runtimeConfig;
    public QuantumMapData mapAsset;
    public EnterRoomParams enterRoomParams;
    public DeterministicSessionConfig deterministicSessionConfig;
    public Boolean Spectate = false;

    public Boolean IsRejoining { get; set; }

    public PlayerPrefString lastSelectedRegion = new PlayerPrefString("LAST_REGION","eu");
    public PlayerPrefString lastUserName = new PlayerPrefString("LAST_USER_NAME","Playerdsadsa");
    public PlayerPrefInt lastSelectedAppVersion = new PlayerPrefInt("LAST_APP_VERSION",0);
    
    public void SetRoomParams(string roomName)
    {
        enterRoomParams = new EnterRoomParams();
        enterRoomParams.RoomOptions = new RoomOptions();
        if(!string.IsNullOrEmpty(roomName))
            enterRoomParams.RoomName = roomName;
        enterRoomParams.RoomOptions.IsVisible  = true;
        var clampedMaxPlayers = 10;
        enterRoomParams.RoomOptions.MaxPlayers = (byte) clampedMaxPlayers;
        enterRoomParams.RoomOptions.Plugins    = new string[] { "QuantumPlugin" };
        enterRoomParams.RoomOptions.CustomRoomProperties = new ExitGames.Client.Photon.Hashtable {
            { "HIDE-ROOM", false },
            { "MAP-GUID", runtimeConfig.Map.Id.Value },
        };
        enterRoomParams.RoomOptions.PlayerTtl = PhotonServerSettings.Instance.PlayerTtlInSeconds * 1000;
        enterRoomParams.RoomOptions.EmptyRoomTtl = PhotonServerSettings.Instance.EmptyRoomTtlInSeconds * 1000;
    }

    public void StartQuantumGame()
    {
        var config = runtimeConfig;

        var param = new QuantumRunner.StartParameters
        {
            RuntimeConfig = config,
            DeterministicConfig = deterministicSessionConfig,
            ReplayProvider = null,
            GameMode = Spectate
                ? Photon.Deterministic.DeterministicGameMode.Spectating
                : Photon.Deterministic.DeterministicGameMode.Multiplayer,
            FrameData = IsRejoining ? InGameWindow.instance.FrameSnapshot : null,
            InitialFrame = IsRejoining ? InGameWindow.instance.FrameSnapshotNumber : 0,
            PlayerCount = 10,
            //LocalPlayerCount = Spectate ? 0 : 1,
            RecordingFlags = RecordingFlags.None,
            NetworkClient = MatchMakingConfig.Instance.Client,
            StartGameTimeoutInSeconds = 10.0f
        };
        
        
        Debug.Log($"Starting QuantumRunner with map guid '{config.Map.Id.Value}' and requesting {param.LocalPlayerCount} player(s).");

        // Joining with the same client id will result in the same quantum player slot which is important for reconnecting.
       // var clientId = ClientIdProvider.CreateClientId(IdProvider, PhotonMain.Client);
        //QuantumRunner.StartGame(clientId, param);

        ReconnectInformation.Refresh(MatchMakingConfig.Instance.Client, TimeSpan.FromMinutes(1));
    }
    
    public void ConnectToPhoton() 
    {
        var appSettings = PhotonServerSettings.CloneAppSettings(PhotonServerSettings.Instance.AppSettings);
        MatchMakingConfig.Instance.Client = new QuantumLoadBalancingClient(PhotonServerSettings.Instance.AppSettings.Protocol);
      
        if (string.IsNullOrEmpty(appSettings.AppIdRealtime.Trim())) {
            //UIDialog.Show("Error", "AppId not set.\n\nSearch or create PhotonServerSettings and configure an AppId.");
            return;
        }
        if (MatchMakingConfig.Instance.Client.ConnectUsingSettings(appSettings)) 
        {
            Debug.Log($"Connecting to nameserver using app settings: '{appSettings.ToStringFull()}'");
            
            var mainMenuTab = GameManager.Instance.SelectTab<MainMenuTab>() as MainMenuTab;
            if (mainMenuTab != null) 
                mainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
        }
        else 
        {
            Debug.LogError($"Failed to connect with app settings: '{appSettings.ToStringFull()}'");
        }

    }

}