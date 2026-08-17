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
   // right before AddPlayer and copied onto RuntimePlayer.Talents.WeaponLevel, which
   // PlayerSpawnUtility.Spawn seeds CharacterStats.WeaponTalentLevel from once at spawn. See
   // PlayerTalents' own comment (RuntimePlayer.User.cs).
   private static readonly PlayerPrefInt WeaponTalentLevelPref = new PlayerPrefInt("weapon_talent_level", 0);

   // Player's own meta-progression reroll-charge talent, same "carried in from outside this match"
   // contract as WeaponTalentLevelPref above - read here right before AddPlayer and copied onto
   // RuntimePlayer.Talents.RerollQuantity, which PlayerSpawnUtility.Spawn seeds CharacterStats.
   // RerollQuantity from once at spawn. See PlayerTalents' own comment (RuntimePlayer.User.cs).
   private static readonly PlayerPrefInt RerollQuantityPref = new PlayerPrefInt("reroll_quantity", 0);

   // Player's own meta-progression Store weapon-offer-count talent, same "carried in from outside
   // this match" contract as WeaponTalentLevelPref/RerollQuantityPref above - read here right
   // before AddPlayer and copied onto RuntimePlayer.Talents.ShopWeaponOfferCount, which
   // PlayerSpawnUtility.Spawn seeds CharacterStats.ShopWeaponOfferCount from once at spawn. See
   // docs/store-blacksmith.md.
   private static readonly PlayerPrefInt ShopWeaponOfferCountPref = new PlayerPrefInt("shop_weapon_offer_count", 0);

   // Player's own meta-progression Starting-Coins talent, same "carried in from outside this
   // match" contract as the talent prefs above - read here right before AddPlayer and copied onto
   // RuntimePlayer.Talents.StartingCoins, which PlayerSpawnUtility.Spawn seeds CharacterStats.Coins
   // from once at spawn (a genuine currency amount, not a 0-5 level, so no byte clamp on the way in
   // like the other talent prefs below get).
   private static readonly PlayerPrefInt StartingCoinsPref = new PlayerPrefInt("starting_coins", 0);

   // Player's own meta-progression Talents (see docs/talents.md), carried in from outside this
   // match the same way as WeaponTalentLevelPref above - read here right before AddPlayer and
   // copied onto RuntimePlayer's own Player*/Has*/Can* fields. One JSON-blob pref (PlayerPrefObject)
   // rather than eighteen separate PlayerPrefInt/PlayerPrefBool fields, since this is now several
   // heterogeneous fields instead of one scalar.
   [Serializable]
   private class TalentSaveData
   {
      public byte PlayerDamageLevel;
      public byte PlayerCooldownLevel;
      public byte PlayerFireRateLevel;
      public byte PlayerReloadSpeedLevel;
      public byte PlayerCriticalChanceLevel;
      public byte PlayerCriticalDamageLevel;
      public byte PlayerMaxHealthLevel;
      public byte PlayerMaxShieldLevel;
      public byte PlayerDamageReductionLevel;
      public byte PlayerMoveSpeedLevel;
      public byte PlayerPickupRangeLevel;
      public byte PlayerExperienceLevel;
      public bool HasWeaponChest;
      public bool HasHeroChest;
      public bool HasGlobalUpgradeChest;
      public bool HasUnlockedRift;
      public bool CanFindStones;
      public bool HasEvent;
   }

   private static readonly PlayerPrefObject<TalentSaveData> TalentsPref =
      new PlayerPrefObject<TalentSaveData>("player_talents", new TalentSaveData());

   public TMP_InputField NameField;
   public TMP_Text ConnectionState;
   public MatchMakingType matchMakingType = MatchMakingType.QUICKPLAY;
   public static MatchMakingConfig Instance;

   // ReconnectInformation gets (re)populated by the SDK on every successful connect - joining/
   // creating a party room included, not just an actual mid-match drop - so it alone doesn't mean
   // there's anything to reconnect to. Only true while NOT currently connected to a room; once
   // actually in a party or a match, there's nothing to reconnect to (you're already there).
   public bool CanReconnect => !Client.InRoom &&
                               matchmakingArguments.ReconnectInformation != null &&
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

      var nameFieldText = NameField != null ? NameField.text : string.Empty;
      Client.NickName = string.IsNullOrWhiteSpace(nameFieldText) ? $"Player{Random.Range(1000, 9999)}" : nameFieldText;

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
      // The party (CUSTOM) flow shows its own "connecting" feedback inline via PartyRoomWidget,
      // staying on MainMenuWindow throughout - only quickplay/reconnect navigate away to the
      // full-screen ConnectingWindow.
      if (matchMakingType != MatchMakingType.CUSTOM)
      {
         var mainMenuTab = GameManager.Instance.MainMenuTab;
         mainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
      }
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
      AlertPopup.Show("Connection Failed", e.Message, () =>
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

   // QuantumRunner.Shutdown() is deferred to that runner's next Service() tick, not synchronous -
   // starting a new SessionRunner before the old one has actually deregistered leaves
   // QuantumRunner.Default resolving to the dead runner, which breaks anything keyed off it
   // (e.g. QuantumHelper.IsLocalPlayer, and therefore the camera never re-binding to the player).
   private async Task WaitForPreviousRunnerToShutdownAsync()
   {
      if (QuantumRunner.Default == null)
         return;

      float startTime = Time.realtimeSinceStartup;
      while (QuantumRunner.Default != null)
      {
         if (Time.realtimeSinceStartup - startTime > GameplaySceneUnloadTimeoutSeconds)
         {
            LogHelper.Error("MatchMaking", "Previous QuantumRunner did not shut down in time - starting new session anyway.");
            return;
         }
         await Task.Yield();
      }
   }

   public async void StartRunner()
   {
      GameManager.Instance.MainMenuTab.windowManager.ShowWindow<ConnectingWindow>();

      try
      {
         await WaitForPreviousGameplaySceneToUnloadAsync();
         await WaitForPreviousRunnerToShutdownAsync();

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

         // Clamp - PlayerPrefInt stores a plain int, PlayerTalents.WeaponLevel is a byte.
         byte weaponTalentLevel = (byte)Mathf.Clamp(WeaponTalentLevelPref.Value, 0, byte.MaxValue);
         byte rerollQuantity = (byte)Mathf.Clamp(RerollQuantityPref.Value, 0, byte.MaxValue);
         byte shopWeaponOfferCount = (byte)Mathf.Clamp(ShopWeaponOfferCountPref.Value, 0, byte.MaxValue);
         int startingCoins = Mathf.Max(StartingCoinsPref.Value, 0);
         TalentSaveData talents = TalentsPref.Value;
         AssetRef<EntityPrototype> localCharacterAvatar = PartyManager.Instance.ResolveLocalCharacterAvatar();

         // PlayerPrefInt has no way to tell "never saved, returned its 0 default" apart from "an
         // account screen genuinely saved 0" (see PlayerPrefProperty.cs - .Value always returns a
         // value, existence isn't exposed). Since nothing writes any of these prefs yet (same
         // pre-existing gap every talent pref here has - an account/profile screen elsewhere would
         // be what actually raises them), a strict overwrite silently stomped whatever was hand-set
         // directly on RuntimePlayers[i].Talents in the Inspector for local testing - exactly the
         // "set Starting Coins in the Inspector, still spawned with 0" bug. Only overwriting when
         // the pref is actually > 0 keeps a real future write taking effect while leaving
         // Inspector-set test values alone until then.
         for (int i = 0; i < RuntimePlayers.Count; i++) {
            RuntimePlayers[i].PlayerAvatar = localCharacterAvatar;
            if (weaponTalentLevel > 0) RuntimePlayers[i].Talents.WeaponLevel = weaponTalentLevel;
            if (rerollQuantity > 0) RuntimePlayers[i].Talents.RerollQuantity = rerollQuantity;
            if (shopWeaponOfferCount > 0) RuntimePlayers[i].Talents.ShopWeaponOfferCount = shopWeaponOfferCount;
            if (startingCoins > 0) RuntimePlayers[i].Talents.StartingCoins = startingCoins;
            RuntimePlayers[i].Talents.PlayerDamageLevel = talents.PlayerDamageLevel;
            RuntimePlayers[i].Talents.PlayerCooldownLevel = talents.PlayerCooldownLevel;
            RuntimePlayers[i].Talents.PlayerFireRateLevel = talents.PlayerFireRateLevel;
            RuntimePlayers[i].Talents.PlayerReloadSpeedLevel = talents.PlayerReloadSpeedLevel;
            RuntimePlayers[i].Talents.PlayerCriticalChanceLevel = talents.PlayerCriticalChanceLevel;
            RuntimePlayers[i].Talents.PlayerCriticalDamageLevel = talents.PlayerCriticalDamageLevel;
            RuntimePlayers[i].Talents.PlayerMaxHealthLevel = talents.PlayerMaxHealthLevel;
            RuntimePlayers[i].Talents.PlayerMaxShieldLevel = talents.PlayerMaxShieldLevel;
            RuntimePlayers[i].Talents.PlayerDamageReductionLevel = talents.PlayerDamageReductionLevel;
            RuntimePlayers[i].Talents.PlayerMoveSpeedLevel = talents.PlayerMoveSpeedLevel;
            RuntimePlayers[i].Talents.PlayerPickupRangeLevel = talents.PlayerPickupRangeLevel;
            RuntimePlayers[i].Talents.PlayerExperienceLevel = talents.PlayerExperienceLevel;
            RuntimePlayers[i].Talents.HasWeaponChest = talents.HasWeaponChest;
            RuntimePlayers[i].Talents.HasHeroChest = talents.HasHeroChest;
            RuntimePlayers[i].Talents.HasGlobalUpgradeChest = talents.HasGlobalUpgradeChest;
            RuntimePlayers[i].Talents.HasUnlockedRift = talents.HasUnlockedRift;
            RuntimePlayers[i].Talents.CanFindStones = talents.CanFindStones;
            RuntimePlayers[i].Talents.HasEvent = talents.HasEvent;
            LogHelper.Log("CharacterSelect", $"AddPlayer(local slot {i}) - PlayerAvatar={RuntimePlayers[i].PlayerAvatar.Id.Value}");
            runner.Game.AddPlayer(i, RuntimePlayers[i]);
         }

         GameManager.Instance.MainMenuTab.windowManager.ShowWindow<InMatchWindow>();
      }
      catch (Exception e)
      {
         LogHelper.Error("MatchMaking", $"StartRunner failed: {e}");
         AlertPopup.Show("Error", "Failed to start the game.", () =>
         {
            GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         });
      }
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
         AlertPopup.Show("Disconnected", cause.ToString(), () =>
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
}
