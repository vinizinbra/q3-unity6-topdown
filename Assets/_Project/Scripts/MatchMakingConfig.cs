using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Photon.Client;
using Photon.Deterministic;
using Photon.Deterministic.Protocol;
using NaughtyAttributes;
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

   // Photon identifies the inactive actor a rejoin reclaims BY UserId, so a fresh Guid every app
   // launch makes reconnect structurally impossible: ReconnectToRoomAsync rejects the attempt
   // outright ("UserId mismatch", since the saved ReconnectInformation.UserId is the PREVIOUS
   // session's guid), and even past that guard the server would find no matching inactive actor
   // and silently let the client in as a brand-new one instead. Generated once, then reused for
   // the lifetime of the install. Photon's own menu SDK sidesteps this by leaving AuthValues null
   // entirely (see QuantumMenuConnectionBehaviourSDK) - we need a real UserId for couch co-op
   // slot identity, so we persist ours instead.
   // Suffixed per local instance - Multiplayer Play Mode virtual players share one PlayerPrefs
   // store, so an unsuffixed key hands every virtual player the SAME UserId and Photon rejects the
   // second one as an active joiner for the same user. See LocalClientIdentity.
   private static readonly PlayerPrefString UserIdPref =
      new PlayerPrefString("photon_user_id" + LocalClientIdentity.PrefSuffix, "");

   // MatchmakingReconnectInformation.DefaultTimeout ships at 20 seconds and Set() only ever runs
   // on a successful join - so out of the box the saved reconnect window expires 20 seconds INTO a
   // match, long before any realistic disconnect, and CanReconnect below silently goes false.
   // Used only when PhotonServerSettings has no positive PlayerTtlInSeconds to derive it from.
   private const int FallbackReconnectWindowSeconds = 60;

   // How often the saved reconnect information is re-stamped while in a room. Without this the
   // window is anchored to the JOIN instant rather than the live session, so a long match is
   // unreconnectable no matter how generous DefaultTimeout is.
   private const float ReconnectInfoRefreshIntervalSeconds = 5f;

   // Set true by CleanReconnectConfig so the refresh below can't resurrect reconnect information
   // for a room the player deliberately left (Disconnect is async - Client.InRoom stays true for
   // a frame or two after the call). Cleared on the next real connect/reconnect attempt.
   private bool _suppressReconnectRefresh;
   private float _nextReconnectInfoRefreshTime;

   // Guards against a double session start: on the plain-join reconnect fallback Photon DOES
   // re-send the cached StartGame event (OnEvent -> StartRunner) while ReconnectAsync is still
   // awaiting, so both paths can fire for the same reconnect. Reset in OnDisconnected.
   private bool _runnerStartRequested;

   // Guards ReconnectAsync against overlapping calls - see its own comment.
   private bool _reconnectInFlight;

   // Lets an in-flight reconnect be aborted. ReconnectToRoomAsync's retry loop keeps issuing
   // RejoinRoom operations on the SHARED Client for up to 10 iterations; without a way to cancel
   // it, starting a normal match while it is still running leaves the two fighting over the same
   // connection - the abandoned operation eventually times out and reports a failure for a match
   // the player is by then already playing. See CancelPendingReconnect.
   private CancellationTokenSource _reconnectCancellation;

   // Written onto the room itself the moment the leader starts the run. Photon deliberately does
   // NOT re-send a room's cached events to a REJOINING actor (only to a plain join), so the cached
   // StartGame event alone can't tell a reconnecting client whether the match is actually running
   // - room properties, unlike cached events, are always part of the room state a rejoin receives.
   public const string PropKeyMatchStarted = "started";

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

   // Player's own meta-progression self-revive-charge talent, same "carried in from outside this
   // match" contract as WeaponTalentLevelPref/RerollQuantityPref above - read here right before
   // AddPlayer and copied onto RuntimePlayer.Talents.SelfReviveCharges, which PlayerSpawnUtility.
   // Spawn seeds CharacterStats.SelfReviveCharges from once at spawn. See docs/revive.md.
   private static readonly PlayerPrefInt SelfReviveChargesPref = new PlayerPrefInt("self_revive_charges", 0);

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
         UserId = ResolvePersistentUserId(),
         // Must be non-null before Connect for ConnectToRoomAsync to populate/persist it on a successful join (see MatchmakingExtensions.ConnectToRoomAsync).
         ReconnectInformation = LocalReconnectInformation.Load(),
         // Without these the room is destroyed the instant a player disconnects (both default to 0), so there is nothing left to reconnect to.
         PlayerTtlInSeconds = globalSettings != null ? globalSettings.PlayerTtlInSeconds : 0,
         EmptyRoomTtlInSeconds = globalSettings != null ? globalSettings.EmptyRoomTtlInSeconds : 0
      };

      // The server drops an inactive actor once PlayerTtl runs out, so that is the true ceiling on
      // how long a reconnect can possibly succeed - match the client-side window to it rather than
      // leaving the SDK's 20s default in place. -1 (keep the actor for the room's lifetime) has no
      // finite ceiling to mirror, so it falls back to the same constant as an unset value.
      if (matchmakingArguments.ReconnectInformation != null)
      {
         int playerTtl = globalSettings != null ? globalSettings.PlayerTtlInSeconds : 0;
         matchmakingArguments.ReconnectInformation.DefaultTimeout =
            playerTtl > 0 ? playerTtl : FallbackReconnectWindowSeconds;
      }

      Instance = this;
   }

   // See UserIdPref above. PlayerPrefString has no "was it ever written" query (.Value always
   // returns a value, existence isn't exposed), so an empty string is what stands in for "never
   // generated" - the same limitation every other pref here works around.
   private static string ResolvePersistentUserId()
   {
      string userId = UserIdPref.Value;
      if (string.IsNullOrEmpty(userId))
      {
         userId = Guid.NewGuid().ToString();
         UserIdPref.Value = userId;
         LogHelper.Log("MatchMaking", $"Generated a new persistent Photon UserId: {userId}" +
            (string.IsNullOrEmpty(LocalClientIdentity.InstanceId) ? "" : $" (virtual player '{LocalClientIdentity.InstanceId}')"));
      }

      return userId;
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
      CancelPendingReconnect("the reconnect information was cleared");
      _suppressReconnectRefresh = true;
      if(matchmakingArguments.ReconnectInformation != null)
         matchmakingArguments.ReconnectInformation.Timeout = DateTime.Now;
      LocalReconnectInformation.Reset();
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
      // Starting a normal match supersedes any reconnect still grinding through its retry loop -
      // both drive the SAME Client, so leaving the old one running is what produced stray
      // "Operation timed out RejoinRoom" failures minutes into an unrelated match.
      CancelPendingReconnect("a normal connect was started");

      _suppressReconnectRefresh = false;

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
      // Two overlapping reconnects would drive the same shared Client against each other, and
      // async void makes an in-flight call otherwise invisible to every caller.
      if (_reconnectInFlight)
      {
         LogHelper.Warn("MatchMaking", "ReconnectAsync: a reconnect is already in flight - ignoring the duplicate request.");
         return;
      }

      if (Client.InRoom)
      {
         LogHelper.Warn("MatchMaking", $"ReconnectAsync: already in room '{Client.CurrentRoom?.Name}' - nothing to reconnect to.");
         return;
      }

      _reconnectInFlight = true;
      _suppressReconnectRefresh = false;

      _reconnectCancellation?.Dispose();
      _reconnectCancellation = AsyncSetup.CreateLinkedSource(CancellationToken.None);

      var asyncConfig = new AsyncConfig
      {
         TaskFactory = AsyncConfig.CreateUnityTaskFactory(),
         CancellationToken = _reconnectCancellation.Token
      };

      var mainMenuTab = GameManager.Instance.MainMenuTab;
      mainMenuTab.windowManager.ShowWindow<ConnectingWindow>();
      try
      {
         await PerformSingleRejoinAsync(asyncConfig);
         LogHelper.Log("MatchMaking", $"Reconnected as {Client.UserId} to room '{Client.CurrentRoom?.Name}' (match started: {HasMatchStarted})");

         // Nothing else brings the simulation back up: Photon does not re-send a room's cached
         // events (StartGame among them) to a REJOINING actor, so the OnEvent -> StartRunner route
         // every normal match start relies on never fires here and the player would otherwise sit
         // on ConnectingWindow forever. StartRunner is idempotent regardless.
         if (HasMatchStarted)
         {
            StartRunner();
         }
         else
         {
            // Rejoined a party room whose run never started - land back in the party screen
            // rather than spinning up a session the rest of the party isn't in.
            mainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         }
      }
      catch (OperationCanceledException)
      {
         // Deliberately cancelled (the player started a normal match instead, or left the party) -
         // whatever replaced it owns the UI now, so this must not pop an error over it.
         LogHelper.Warn("MatchMaking", "Reconnect cancelled.");
      }
      catch (Exception e)
      {
         HandleConnectFailure(e);
      }
      finally
      {
         _reconnectInFlight = false;
      }
   }

   // ONE rejoin attempt, deliberately replacing MatchmakingExtensions.ReconnectToRoomAsync.
   //
   // That method is not a single attempt: it first tries a ReconnectAndRejoin fast path, then falls
   // into a loop of up to 10 join attempts, disconnecting and re-connecting the shared Client
   // between them. Two failure modes come out of that, both observed here:
   //
   //  - The fast path can succeed ON THE WIRE while its await reports otherwise (a client-side
   //    operation timeout, for instance). The client is in the room and the cached StartGame event
   //    has already started the match - and the SDK then disconnects it and rejoins from scratch,
   //    because as far as the loop is concerned nothing has succeeded yet.
   //  - Whichever redundant operation loses that race never gets a response and surfaces much later
   //    as "Operation timed out RejoinRoom", long after the player moved on.
   //
   // So: check whether we are already where we want to be, otherwise connect and issue exactly one
   // RejoinRoom, and report whatever comes back. No retries, no fallback join as a new actor.
   private async Task PerformSingleRejoinAsync(AsyncConfig asyncConfig)
   {
      var info = matchmakingArguments.ReconnectInformation;

      if (info == null || string.IsNullOrEmpty(info.Room))
         throw new InvalidOperationException("No reconnect information saved - there is no room to rejoin.");

      if (string.IsNullOrEmpty(info.UserId))
         throw new InvalidOperationException("Saved reconnect information has no UserId.");

      if (matchmakingArguments.AuthValues != null && info.UserId != matchmakingArguments.AuthValues.UserId)
         throw new InvalidOperationException($"UserId mismatch - saved '{info.UserId}', current '{matchmakingArguments.AuthValues.UserId}'.");

      if (!matchmakingArguments.CanRejoin)
         throw new InvalidOperationException("PlayerTtlInSeconds is 0 - the server keeps no inactive actor to rejoin.");

      // Already there. Never tear down a connection that already satisfies the goal - that is the
      // exact move that turned a succeeded reconnect back into a disconnect.
      if (Client.InRoom && string.Equals(Client.CurrentRoom?.Name, info.Room, StringComparison.Ordinal))
      {
         LogHelper.Warn("MatchMaking", $"Rejoin skipped - already in room '{info.Room}'.");
         info.Set(Client);
         return;
      }

      // A rejoin can only be issued from master/lobby, so anything else has to be dropped first.
      if (Client.IsConnected &&
          Client.State != ClientState.ConnectedToMasterServer &&
          Client.State != ClientState.JoinedLobby)
      {
         await Client.DisconnectAsync(asyncConfig);
      }

      if (!Client.IsConnected)
      {
         if (matchmakingArguments.AuthValues != null)
            Client.AuthValues = matchmakingArguments.AuthValues.CopyTo(new AuthenticationValues());

         Client.CrcEnabled = matchmakingArguments.EnableCrc;
         matchmakingArguments.PhotonSettings.FixedRegion = info.Region;

         await Client.ConnectUsingSettingsAsync(matchmakingArguments.PhotonSettings, asyncConfig);
      }

      LogHelper.Log("MatchMaking", $"Rejoining room '{info.Room}' as '{info.UserId}' (single attempt).");

      short result = await Client.RejoinRoomAsync(info.Room, ticket: matchmakingArguments.Ticket, throwOnError: false, config: asyncConfig);

      if (result != ErrorCode.Ok)
         throw new OperationException(result, DescribeRejoinFailure(result, info.Room));

      info.Set(Client);
   }

   // Photon's raw rejoin error codes say nothing to a player - AlertPopup shows this text.
   private static string DescribeRejoinFailure(short errorCode, string room)
   {
      switch (errorCode)
      {
         case ErrorCode.GameDoesNotExist:
            return $"Room '{room}' no longer exists.";
         case ErrorCode.JoinFailedWithRejoinerNotFound:
            return $"Your slot in room '{room}' expired.";
         case ErrorCode.JoinFailedFoundActiveJoiner:
            return $"Another client is already connected as this user in room '{room}'.";
         case ErrorCode.GameFull:
            return $"Room '{room}' is full.";
         case ErrorCode.GameClosed:
            return $"Room '{room}' is closed.";
         default:
            return $"Rejoining room '{room}' failed.";
      }
   }

   // Aborts an in-flight ReconnectAsync. Safe to call when none is running.
   public void CancelPendingReconnect(string reason)
   {
      if (!_reconnectInFlight)
         return;

      LogHelper.Warn("MatchMaking", $"Cancelling the in-flight reconnect ({reason}).");
      _reconnectCancellation?.Cancel();
   }

   private void HandleConnectFailure(Exception e)
   {
      // A connection operation can time out or error AFTER the client has already ended up in the
      // room by another route - most often a redundant RejoinRoom left over from
      // ReconnectToRoomAsync's own retry loop, whose response is never delivered once Quantum's
      // communicator owns the connection. Reporting that as a failure would pull a player out of a
      // match they are actively playing, which is far worse than the stale error it is reporting.
      if (Client != null && Client.InRoom)
      {
         LogHelper.Warn("MatchMaking", $"Ignoring a connection failure that arrived after the client was already in room '{Client.CurrentRoom?.Name}': {e.Message}");
         return;
      }

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

      RefreshReconnectInformation();

      Client?.Service();
   }

   // MatchmakingReconnectInformation.Set() is only ever called by the SDK on a successful join, so
   // the saved window is anchored to the JOIN instant - five minutes into a match it has long since
   // expired and CanReconnect is false, which is why the Play button never offered a reconnect.
   // Re-stamping it while in a room keeps the window tracking the live session instead.
   private void RefreshReconnectInformation()
   {
      if (_suppressReconnectRefresh) return;
      if (matchmakingArguments.ReconnectInformation == null) return;
      if (Client == null || !Client.InRoom || Client.CurrentRoom == null) return;
      if (Time.unscaledTime < _nextReconnectInfoRefreshTime) return;

      _nextReconnectInfoRefreshTime = Time.unscaledTime + ReconnectInfoRefreshIntervalSeconds;

      // LocalReconnectInformation.Set overrides this to persist AND flush to disk (the SDK's own
      // QuantumReconnectInformation only calls PlayerPrefs.SetString, which survives nothing but a
      // clean quit - see LocalReconnectInformation).
      matchmakingArguments.ReconnectInformation.Set(Client);
   }

   // CanReconnect is an AND of three separate conditions, none of which is otherwise visible while
   // testing - this prints each one plus the saved information behind it.
   [Button("Log Reconnect State")]
   public void LogReconnectState()
   {
      var info = matchmakingArguments.ReconnectInformation;
      if (info == null)
      {
         LogHelper.Warn("MatchMaking", "Reconnect state: ReconnectInformation is NULL (Awake never ran?).");
         return;
      }

      LogHelper.Log("MatchMaking",
         $"Reconnect state: CanReconnect={CanReconnect}" +
         $" | InRoom={Client?.InRoom} (must be false)" +
         $" | HasTimedOut={info.HasTimedOut} (must be false)" +
         $" | Room='{info.Room}' Region='{info.Region}' UserId='{info.UserId}'" +
         $" | Timeout={info.Timeout} (now={DateTime.Now}) Window={info.DefaultTimeout}s" +
         $" | Suppressed={_suppressReconnectRefresh}" +
         $" | Instance='{(string.IsNullOrEmpty(LocalClientIdentity.InstanceId) ? "main" : LocalClientIdentity.InstanceId)}'" +
         $" LocalUserId='{matchmakingArguments.UserId}'");
   }

   // Whether the room this client is in has actually started its run (see PropKeyMatchStarted).
   // Distinguishes reconnecting INTO a live match from rejoining a party room still sitting in the
   // lobby - the latter must not start a session nobody else is in.
   private bool HasMatchStarted
   {
      get
      {
         var properties = Client?.CurrentRoom?.CustomProperties;
         return properties != null
                && properties.TryGetValue(PropKeyMatchStarted, out var stored)
                && stored is bool started
                && started;
      }
   }
  

   public void StartQuantumGame()
   {
      // See PropKeyMatchStarted - a rejoining client never receives the cached event below, but it
      // always receives the room's properties, so this is what tells it the run is actually live.
      Client.CurrentRoom?.SetCustomProperties(new PhotonHashtable { { PropKeyMatchStarted, true } });

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
      // Both the cached StartGame event and ReconnectAsync can ask for a session start for the
      // same reconnect (see ReconnectAsync) - and StartRunner is async void, so an in-flight start
      // is not otherwise observable. First request wins; the rest are dropped.
      if (_runnerStartRequested)
      {
         LogHelper.Warn("MatchMaking", "StartRunner: a session start is already in flight - ignoring the duplicate request.");
         return;
      }

      _runnerStartRequested = true;

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
         byte selfReviveCharges = (byte)Mathf.Clamp(SelfReviveChargesPref.Value, 0, byte.MaxValue);
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
            if (selfReviveCharges > 0) RuntimePlayers[i].Talents.SelfReviveCharges = selfReviveCharges;
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
         _runnerStartRequested = false;
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
      // The session this guard was protecting is over either way - a reconnect has to be able to
      // request a fresh one.
      _runnerStartRequested = false;

      // Without this the only trace a disconnect leaves is a WindowManager line, and telling a
      // server-side eviction apart from a deliberate Client.Disconnect() means reading stack-trace
      // line numbers to work out which branch below ran. DisconnectByClientLogic means THIS client
      // asked to disconnect - including via InMatchWindow's plugin-disconnect popup, so it is not
      // by itself proof that the player chose to leave.
      LogHelper.Warn("MatchMaking", $"OnDisconnected: cause={cause} | room='{Client?.CurrentRoom?.Name}'" +
         $" | instance='{(string.IsNullOrEmpty(LocalClientIdentity.InstanceId) ? "main" : LocalClientIdentity.InstanceId)}'" +
         $" | userId='{matchmakingArguments.UserId}'");

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
