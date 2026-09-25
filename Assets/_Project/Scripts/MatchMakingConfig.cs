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

   // The RuntimeConfig.Seed that seeds Frame.RNG for this match, rolled ONCE by whichever client
   // calls StartQuantumGame (always the master client - see PartyManager/WaitingForPlayersWindow)
   // and carried to every other client via the same room-property channel as PropKeyMatchStarted.
   // Required because every client here runs StartRunner() independently off its own LOCAL
   // RuntimeConfig field (Seed: 0 on the scene asset, never otherwise assigned) - without this,
   // each client would start the deterministic sim on a different (or, worse, an identical but
   // never-randomized) seed. Room properties, not the cached StartGame event, so a reconnecting
   // client picks up the same value too (see PropKeyMatchStarted's own comment).
   public const string PropKeySeed = "seed";

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

   // The room the party actually lives in, distinct from whatever single-use match room a run
   // happens to be playing in (see StartMatchInNewRoom/MoveToMatchRoomAsync). Set once, when the
   // party is first created/joined (PartyManager.BeginConnect). ReturnToPartyLobby rejoins THIS
   // room by name specifically, not just "whatever room we were last in" - the two are the same
   // room only while actually sitting in the lobby.
   //
   // Why a match needs its own room at all: Quantum's server plugin ties its deterministic-session
   // bookkeeping (buddy-snapshot/reconnect support) to the ROOM, keyed by the persistent ClientId
   // this project intentionally reuses across the whole app lifetime for couch-co-op slot identity
   // (see ResolvePersistentUserId). Starting a SECOND SessionRunner in a room that already hosted a
   // finished match gets treated by the server as a late-join into that same game and tries to
   // request a buddy snapshot - which fails, because every client already tore its runner down when
   // the match legitimately ended (confirmed against Photon's own docs: "the buddy snapshot process
   // is started automatically when any client is starting its QuantumRunner... Error #13: Snapshot
   // request failed - there is no other client in the room/game that can send a buddy snapshot").
   // A fresh room per match sidesteps this entirely; the party room itself never runs a Quantum
   // session at all, so it's never subject to any of this.
   public string PartyRoomCode { get; set; }

   // The UserId of whoever was party leader when the CURRENT/last match started - captured from
   // the one client allowed to call StartMatchInNewRoom (only the leader can) and broadcast to
   // everyone via the SyncMatchRoom event payload, so every client remembers it locally through the
   // whole match. Needed because Photon's own MasterClientId doesn't survive the party room being
   // destroyed/recreated (see PartyRoomCode's comment) - without this, plain default election
   // (lowest actor number in the recreated room) hands leadership to whoever gets back first,
   // regardless of who led before. See ReclaimLeadershipIfNeeded.
   public string DesignatedLeaderUserId { get; private set; }

   public bool IsInPartyRoom => Client != null && Client.CurrentRoom != null &&
      string.Equals(Client.CurrentRoom.Name, PartyRoomCode, StringComparison.Ordinal);

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
         bool reconnectedIntoMatch = !string.IsNullOrEmpty(PartyRoomCode) && !IsInPartyRoom;
         LogHelper.Log("MatchMaking", $"Reconnected as {Client.UserId} to room '{Client.CurrentRoom?.Name}' (match room: {reconnectedIntoMatch})");

         // Nothing else brings the simulation back up: a rejoin into the match room means a genuine
         // mid-match reconnect (that room is single-use and only ever hosts one session - see
         // PartyRoomCode's comment), so StartRunner has to be triggered explicitly here; it reads
         // the seed back from PropKeySeed (see MoveToMatchRoomAsync). StartRunner is idempotent
         // regardless.
         if (reconnectedIntoMatch)
         {
            StartRunner();
         }
         else
         {
            // Rejoined the party room itself - land back in the party screen rather than spinning
            // up a session the rest of the party isn't in.
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
   //
   // NOTE: only the legacy QUICKPLAY/WaitingForPlayersWindow path (unused by the live
   // PartyManager-driven CUSTOM flow - see docs/party-matchmaking-report.md) still reads/writes
   // PropKeyMatchStarted via StartQuantumGame below. The CUSTOM flow now uses a fresh, single-use
   // match room per run (StartMatchInNewRoom/MoveToMatchRoomAsync) instead of restarting a session
   // in a room that already hosted one - see PartyRoomCode's own comment for why that's required.

   public void StartQuantumGame()
   {
      // See PropKeySeed - rolled once here, by the one client that calls StartQuantumGame, so
      // every client's StartRunner() picks up the SAME value instead of each seeding its own local
      // RuntimeConfig.Seed (which stays at its 0 scene default).
      int seed = Guid.NewGuid().GetHashCode();

      // See PropKeyMatchStarted - a rejoining client never receives the cached event below, but it
      // always receives the room's properties, so this is what tells it the run is actually live.
      Client.CurrentRoom?.SetCustomProperties(new PhotonHashtable
      {
         { PropKeyMatchStarted, true },
         { PropKeySeed, seed }
      });

      Client.OpRaiseEvent((byte)110,1,
         new RaiseEventArgs() { Receivers = ReceiverGroup.All , CachingOption = EventCaching.AddToRoomCacheGlobal},
         SendOptions.SendReliable);
   }

   // Leader-only. Generates a brand-new, single-use match room name + seed and broadcasts it (an
   // uncached event - see its own reasoning below) to everyone currently in the party room, which
   // MoveToMatchRoomAsync then carries out on every client including this one (ReceiverGroup.All
   // includes the sender, same as the legacy StartQuantumGame's own event above). This replaces
   // the old single-room start entirely for the party/CUSTOM flow - see PartyRoomCode's comment
   // for why reusing one room across matches doesn't work with Quantum's session model.
   public void StartMatchInNewRoom()
   {
      if (Client == null || Client.LocalPlayer == null || !Client.LocalPlayer.IsMasterClient)
         return;

      string matchRoomCode = Guid.NewGuid().ToString("N").Substring(0, 8);
      int seed = Guid.NewGuid().GetHashCode();

      // Deliberately NOT cached (unlike the old StartGame event): this room gets abandoned the
      // moment the match ends and is never rejoined, so there's no later (re)join that should ever
      // receive this again - caching it was exactly the bug that caused a finished match to
      // instantly restart on return (see git history / the investigation that led here).
      //
      // "leader" is this client's own UserId - only the leader can reach this point at all (the
      // IsMasterClient guard above), so it's necessarily the leader's own ID. Carried to everyone
      // so DesignatedLeaderUserId survives the party room being destroyed/recreated after a long
      // match - see its own comment and ReclaimLeadershipIfNeeded.
      Client.OpRaiseEvent((byte)PhotonEventCode.SyncMatchRoom,
         new PhotonHashtable { { "room", matchRoomCode }, { "seed", seed }, { "leader", Client.UserId } },
         new RaiseEventArgs { Receivers = ReceiverGroup.All },
         SendOptions.SendReliable);
   }

   // Called once this client has landed back in the party room (see PartyManager.HandleJoinedOrCreated)
   // - hands master client status back to whoever led the party into the match that just ended, if
   // Photon's own default election (lowest actor number in the recreated room) gave it to someone
   // else instead. A no-op for every client except the one whose UserId matches - SetMasterClient
   // is a CAS write against the room's current MasterClientId, safe even if called redundantly.
   public void ReclaimLeadershipIfNeeded()
   {
      if (Client == null || Client.CurrentRoom == null || Client.LocalPlayer == null)
         return;

      if (string.IsNullOrEmpty(DesignatedLeaderUserId) || DesignatedLeaderUserId != Client.UserId)
         return;

      if (Client.LocalPlayer.IsMasterClient)
         return;

      Client.CurrentRoom.SetMasterClient(Client.LocalPlayer);
   }

   // Builds RoomOptions/EnterRoomArgs matching this app's matchmakingArguments (MaxPlayers,
   // PlayerTtl/EmptyRoomTtl, the QuantumPlugin) for an ad hoc join/create outside the normal
   // Quickplay/ConnectToRoomAsync pipeline - that pipeline always does a full (re)connect even when
   // already connected, which is wrong for a same-session room-to-room hop while already talking to
   // the master server. Mirrors Photon's own (private) MatchmakingArguments.BuildEnterRoomArgs.
   private EnterRoomArgs BuildRoomArgs(string roomName)
   {
      return new EnterRoomArgs
      {
         RoomName = roomName,
         RoomOptions = new RoomOptions
         {
            MaxPlayers = (byte)matchmakingArguments.MaxPlayers,
            PlayerTtl = matchmakingArguments.PlayerTtlInSeconds * 1000,
            EmptyRoomTtl = matchmakingArguments.EmptyRoomTtlInSeconds * 1000,
            Plugins = matchmakingArguments.Plugins,
         }
      };
   }

   // Every client's reaction to StartMatchInNewRoom's event (see OnEvent below): leave the party
   // room and join/create the fresh match room together, then start simulating immediately - no
   // PropKeyMatchStarted/cached-event dance needed here, since this room is used for exactly one
   // SessionRunner ever. PropKeySeed is still written (master-only) purely as a safety net for a
   // genuine mid-match reconnect later (a real network drop while playing) - StartRunner already
   // knows how to read it back; the seed is normally carried directly via the event payload instead.
   private async void MoveToMatchRoomAsync(string matchRoomCode, int seed)
   {
      try
      {
         await Client.LeaveRoomAsync(becomeInactive: false);
         await Client.JoinOrCreateRoomAsync(BuildRoomArgs(matchRoomCode));
      }
      catch (Exception e)
      {
         LogHelper.Error("MatchMaking", $"MoveToMatchRoomAsync: failed to move into match room '{matchRoomCode}': {e}");
         AlertPopup.Show("Error", "Failed to start the match.", () =>
         {
            GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         });
         return;
      }

      if (Client.LocalPlayer.IsMasterClient)
         Client.CurrentRoom?.SetCustomProperties(new PhotonHashtable { { PropKeySeed, seed } });

      RuntimeConfig.Seed = seed;
      StartRunner();
   }

   // Quantum's own SDK code (QuantumCallbackHandler_UnityCallbacks in QuantumUnityRuntime.cs) unloads
   // the previous match's gameplay scene asynchronously over several frames on a DontDestroyOnLoad
   // host, with no memory of that cleanup carried into the next match's own tracker instance.
   // Starting a new match before that unload finishes leaves two copies of the scene loaded at once -
   // two Main Cameras, two AudioListeners. WaitForPreviousGameplaySceneToUnloadAsync below guards
   // against that race instead of relying on timing to always favor us.
   //
   // Deliberately NOT keyed off a hardcoded gameplay scene name (see QuantumMap.asset's Scene field) -
   // this project already renamed that scene once (QuantumGameScene -> GrasslandOutpostGameScene) and
   // a name check silently stopped matching until someone noticed. Checking "is any OTHER scene
   // loaded besides the menu" answers the same question without depending on what the gameplay scene
   // happens to be called this week - it just has to not be the menu, which GameManager anchors (it
   // lives at MenuScene's root and, unlike this class, is never DontDestroyOnLoad'd out of it).
   private const float GameplaySceneUnloadTimeoutSeconds = 5f;

   private async Task WaitForPreviousGameplaySceneToUnloadAsync()
   {
      Scene stale = FindNonMenuScene();
      if (!stale.IsValid())
         return;

      LogHelper.Warn("MatchMaking", $"'{stale.name}' is still loaded from a previous match - waiting for it to unload before starting a new session.");

      float startTime = Time.realtimeSinceStartup;
      while ((stale = FindNonMenuScene()).IsValid())
      {
         if (Time.realtimeSinceStartup - startTime > GameplaySceneUnloadTimeoutSeconds)
         {
            LogHelper.Error("MatchMaking", $"'{stale.name}' did not unload within {GameplaySceneUnloadTimeoutSeconds}s - starting new session anyway.");
            return;
         }
         await Task.Yield();
      }
   }

   // Any loaded scene other than the menu is either the current/previous match's gameplay scene.
   // GameManager.Instance is null only if GameManager hasn't run its own Awake yet, which can't be
   // true here - StartRunner already depends on it (ShowWindow<LoadingWindow> above) before this is
   // ever reached.
   private static Scene FindNonMenuScene()
   {
      Scene menuScene = GameManager.Instance != null ? GameManager.Instance.gameObject.scene : default;

      for (int i = 0; i < SceneManager.sceneCount; i++)
      {
         Scene scene = SceneManager.GetSceneAt(i);
         if (scene.IsValid() && scene.isLoaded && scene != menuScene)
            return scene;
      }

      return default;
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

      // From here to the moment the hero is actually standing in the world, ONE screen covers
      // everything - including SessionRunner.StartAsync below, which is what additively loads
      // QuantumGameScene and therefore brings that scene's own HUD Canvas (sortingOrder 11) up over
      // every menu Canvas (0). LoadingWindow's own nested Canvas sorts above both, so nothing of a
      // match that hasn't visually started yet can flash through. ConnectingWindow is no longer
      // shown here: its Photon callbacks only produced alerts that MatchMakingConfig.OnDisconnected
      // and the catch below already raise on their own.
      try
      {
         // Inside the try, not before it: if ShowWindow itself throws (e.g. some other window's
         // Hide() blows up on a stale reference), this still has to hit the catch below so
         // _runnerStartRequested gets reset and the player lands back on MainMenuWindow instead of
         // being stuck on whatever was left on screen with the flag wedged true forever.
         GameManager.Instance.MainMenuTab.windowManager.ShowWindow<LoadingWindow>();

         await WaitForPreviousGameplaySceneToUnloadAsync();
         await WaitForPreviousRunnerToShutdownAsync();

         // Overwrite this client's local RuntimeConfig.Seed (0, per the scene default) with the
         // one value every client in the room agreed on - see PropKeySeed. Left untouched (falls
         // back to whatever RuntimeConfig.Seed already is) only if the property is somehow missing,
         // which should never happen on the normal StartQuantumGame -> StartGame event path.
         var roomProperties = Client?.CurrentRoom?.CustomProperties;
         if (roomProperties != null && roomProperties.TryGetValue(PropKeySeed, out var storedSeed) && storedSeed is int seed)
            RuntimeConfig.Seed = seed;
         else
            LogHelper.Warn("MatchMaking", "StartRunner: no seed found in room properties - falling back to RuntimeConfig's own local Seed.");

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
            // ShutdownConnectionOptions.None: by default the communicator disconnects the Photon
            // client itself whenever the runner shuts down (QuantumRunner.ShutdownAll/OnDestroy),
            // which fired out from under ReturnToPartyLobby's own QuantumRunner.ShutdownAll() call
            // and raced its SetLocalReady write against an already-Disconnecting client. LeaveMatch
            // still disconnects explicitly for the real "leave the match" paths, so this only
            // changes what happens on the runner's OWN shutdown, not on an actual leave/quit.
            Communicator = new QuantumNetworkCommunicator(Client, ShutdownConnectionOptions.None)
         };

         var runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);

         AddLocalPlayers(runner);

         // Deliberately NOT ShowWindow<InMatchWindow>() here, which is what this used to do: that
         // window disables the whole menu Canvas (see InMatchWindow.Show), and at this point the
         // level hasn't been generated and no hero has spawned - taking the menu down here is
         // exactly what left the player staring at a half-built, empty level. The LoadingWindow
         // shown above stays up and shows InMatchWindow itself once the local hero is genuinely
         // standing in the world.
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

   // Shared by StartRunner (online) and StartOfflineRunner (offline) - couch co-op talent/avatar
   // setup is identical either way, only how the session itself is started differs.
   //
   // PlayerPrefInt has no way to tell "never saved, returned its 0 default" apart from "an
   // account screen genuinely saved 0" (see PlayerPrefProperty.cs - .Value always returns a
   // value, existence isn't exposed). Since nothing writes any of these prefs yet (same
   // pre-existing gap every talent pref here has - an account/profile screen elsewhere would
   // be what actually raises them), a strict overwrite silently stomped whatever was hand-set
   // directly on RuntimePlayers[i].Talents in the Inspector for local testing - exactly the
   // "set Starting Coins in the Inspector, still spawned with 0" bug. Only overwriting when
   // the pref is actually > 0 keeps a real future write taking effect while leaving
   // Inspector-set test values alone until then.
   private void AddLocalPlayers(QuantumRunner runner)
   {
      // Clamp - PlayerPrefInt stores a plain int, PlayerTalents.WeaponLevel is a byte.
      byte weaponTalentLevel = (byte)Mathf.Clamp(WeaponTalentLevelPref.Value, 0, byte.MaxValue);
      byte rerollQuantity = (byte)Mathf.Clamp(RerollQuantityPref.Value, 0, byte.MaxValue);
      byte shopWeaponOfferCount = (byte)Mathf.Clamp(ShopWeaponOfferCountPref.Value, 0, byte.MaxValue);
      int startingCoins = Mathf.Max(StartingCoinsPref.Value, 0);
      byte selfReviveCharges = (byte)Mathf.Clamp(SelfReviveChargesPref.Value, 0, byte.MaxValue);
      TalentSaveData talents = TalentsPref.Value;
      AssetRef<EntityPrototype> localCharacterAvatar = PartyManager.Instance.ResolveLocalCharacterAvatar();

      // The Inspector-authored PlayerNickname on these entries is never the real player's name
      // (it was leaking e.g. "pixie" onto every hero's CharacterUiWidget). Online, the first
      // human slot takes this client's Photon nickname; offline and extra couch co-op slots get
      // none, so CharView falls back to the hero's name.
      string localNickname = GameManager.Instance.isPlayingOffline ? null : Client?.NickName;

      for (int i = 0; i < RuntimePlayers.Count; i++) {
         // A bot keeps whatever PlayerAvatar was authored on its own entry (see docs/bots.md) -
         // the whole point of filling the party with bots is watching a DIFFERENT hero than the
         // one this client picked, so the character-select choice must not be stamped onto them.
         if (RuntimePlayers[i].IsBot == false)
         {
            RuntimePlayers[i].PlayerAvatar = localCharacterAvatar;
            RuntimePlayers[i].PlayerNickname = localNickname;
            localNickname = null;
         }
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
   }

   // Offline counterpart to StartRunner - starts a purely local Quantum simulation
   // (DeterministicGameMode.Local, no Communicator/Photon room) so a match can be played and
   // tested without any network connection at all. Reuses the exact same RuntimeConfig (map/sim
   // config) and RuntimePlayers (couch co-op slots) Inspector setup as online play - only how the
   // session is started differs. Mirrors QuantumRunnerLocalDebug's own local-start arguments.
   public async void StartOfflineRunner()
   {
      // Same duplicate-start guard as StartRunner - see its own comment. Cleared by LeaveMatch
      // below rather than OnDisconnected, since an offline session never connects to Photon.
      if (_runnerStartRequested)
      {
         LogHelper.Warn("MatchMaking", "StartOfflineRunner: a session start is already in flight - ignoring the duplicate request.");
         return;
      }

      _runnerStartRequested = true;

      try
      {
         // Same reasoning as StartRunner's own comment on this: inside the try, not before it, so a
         // throw from ShowWindow itself still hits the catch below instead of leaving
         // _runnerStartRequested wedged true (which blocks StartRunner too - the flag is shared).
         GameManager.Instance.MainMenuTab.windowManager.ShowWindow<LoadingWindow>();

         await WaitForPreviousGameplaySceneToUnloadAsync();
         await WaitForPreviousRunnerToShutdownAsync();

         var runtimeConfig = new QuantumUnityJsonSerializer().CloneConfig(RuntimeConfig);
         // No room to agree a shared seed with (see PropKeySeed) - roll it locally instead.
         runtimeConfig.Seed = Guid.NewGuid().GetHashCode();

         var sessionConfig = QuantumDeterministicSessionConfigAsset.DefaultConfig;
         if (DisableChecksumsForRelease)
            sessionConfig.ChecksumInterval = 0;

         var sessionRunnerArguments = new SessionRunner.Arguments {
            RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
            GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
            RuntimeConfig = runtimeConfig,
            SessionConfig = sessionConfig,
            GameMode = DeterministicGameMode.Local,
            PlayerCount = OverwritePlayerCount > 0 ? Math.Min(OverwritePlayerCount, Quantum.Input.MAX_COUNT) : Quantum.Input.MAX_COUNT,
         };

         var runner = (QuantumRunner)await SessionRunner.StartAsync(sessionRunnerArguments);

         AddLocalPlayers(runner);
      }
      catch (Exception e)
      {
         _runnerStartRequested = false;
         GameManager.Instance.isPlayingOffline = false;
         LogHelper.Error("MatchMaking", $"StartOfflineRunner failed: {e}");
         AlertPopup.Show("Error", "Failed to start offline game.", () =>
         {
            GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         });
      }
   }

   // Single "leave the current match, go back to the menu" entry point for both online and
   // offline sessions. Online leaves through Client.Disconnect() exactly as before (its
   // OnDisconnected callback tears the runner down and shows MainMenuWindow); offline has no
   // Photon connection to disconnect from, so it has to shut the runner down and navigate back
   // itself - see StartOfflineRunner's own comment on why _runnerStartRequested is cleared here.
   //
   // The generic loading screen goes up FIRST and only then does the teardown start (offline
   // shutdown, or Client.Disconnect() whose OnDisconnected callback shuts the runner down) - so the
   // player never watches the match being torn apart - and stays up until the runner is gone and the
   // gameplay scene has finished unloading. A second click while it is already up is ignored.
   public void LeaveMatch()
   {
      if (SceneLoader.IsBusy)
         return;

      _pendingDisconnectReason = null;

      if (IsInMatch())
      {
         SceneLoader.Cover(PerformLeaveMatch, IsMatchTeardownComplete);
         return;
      }

      PerformLeaveMatch();
   }

   // True while the in-match window is the one on screen, i.e. there is actually a match to tear
   // down. OnDisconnected also fires for disconnects that happen in the menu (party lobby, failed
   // connect), which must NOT raise a loading screen.
   private static bool IsInMatch()
   {
      MainMenuTab tab = GameManager.Instance != null ? GameManager.Instance.MainMenuTab : null;
      return tab != null && tab.windowManager != null && tab.windowManager.currentWindow is InMatchWindow;
   }

   // The runner is shut down AND Quantum's own map-unload coroutine has taken the gameplay scene
   // out (QuantumGame.Dispose starts it) - what the loading screen waits for before lifting.
   private static bool IsMatchTeardownComplete()
   {
      return QuantumRunner.Default == null
         && UnityEngine.SceneManagement.SceneManager.GetSceneByName(GameManager.GameplaySceneName).isLoaded == false;
   }

   private void PerformLeaveMatch()
   {
      if (GameManager.Instance != null && GameManager.Instance.isPlayingOffline)
      {
         GameManager.Instance.isPlayingOffline = false;
         _runnerStartRequested = false;

         if (QuantumRunner.Default != null)
            QuantumRunner.ShutdownAll();

         GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         return;
      }

      // Deliberately does NOT clear the reconnect information: a co-op run is worth rejoining
      // even when you left it on purpose (misclick, stepping away) - see InMatchWindow's own
      // former comment to this effect. Only leaving the party LOBBY clears it (PartyManager.LeaveParty).
      Client.Disconnect();
   }

   // Offline-only "start this run over": shuts the runner down (the Quantum SDK unloads the gameplay
   // scene as part of that) and immediately starts a fresh offline session, which reloads it.
   // StartOfflineRunner already does the rest of the work - it shows LoadingWindow (which takes the
   // menu Canvas back up over the dying match) and waits for the old scene to finish unloading and
   // the old runner to deregister before creating the new one, so nothing needs sequencing here
   // beyond clearing its duplicate-start guard first. isPlayingOffline is deliberately left true.
   public void RestartOfflineMatch()
   {
      if (GameManager.Instance == null || GameManager.Instance.isPlayingOffline == false)
      {
         LogHelper.Warn("MatchMaking", "RestartOfflineMatch: not playing offline - ignoring.");
         return;
      }

      _runnerStartRequested = false;

      if (QuantumRunner.Default != null)
         QuantumRunner.ShutdownAll();

      StartOfflineRunner();
   }

   // Used only by RunResultPopup, where the run has already legitimately ended for everyone -
   // unlike LeaveMatch's other two callers (GameplayUiController/InMatchWindow's mid-match quit
   // buttons), where a live match still needs this player's input and an actual disconnect is the
   // right call.
   //
   // Several same-room approaches were tried and rejected before landing on a separate match room
   // per run (see PartyRoomCode's own comment for the root cause): a full disconnect+later
   // reconnect is bounded by Photon Cloud's hard 300s EmptyRoomTtl cap; staying silently connected
   // in the same room left the Quantum plugin's session bookkeeping stale ("Error #52: Snapshot
   // upload timeout" on the next match); leave-and-rejoin (OpRejoinRoom) itself triggers the
   // plugin's live-match snapshot machinery ("Error #13: Snapshot request failed to start"); even a
   // plain leave+join of the SAME room hit the identical Error #13, because the trigger was never
   // the Photon-level operation at all - it's Quantum's per-room deterministic-session state.
   //
   // So: leave the ephemeral MATCH room for good (never rejoined - PlayerTtl/becomeInactive don't
   // matter here) and join-or-create the party's own room by its remembered code instead. Joining
   // a room that never ran a Quantum session at all sidesteps the plugin entirely. A plain join
   // creates a fresh actor, so per-player custom properties don't carry over automatically -
   // PartyManager.OnJoinedRoom republishes the local character pick for this reason.
   // Guards against overlapping calls - unlike LeaveMatch's plain Client.Disconnect() (harmless if
   // called twice), a leave-then-join sequence is NOT safe to run twice concurrently: the second
   // call's LeaveRoomAsync finds the client already out of the room (the first call already left
   // it) and throws OperationStartException("Must be inside a room") - observed from a fast
   // double-click on RunResultPopup's Leave/Continue button, which had no debounce of its own.
   private bool _returningToPartyLobby;

   //
   // Covered by the generic loading screen like LeaveMatch: screen first, then the teardown, held
   // until the async leave/join has finished AND the gameplay scene is gone.
   public void ReturnToPartyLobby()
   {
      if (_returningToPartyLobby || SceneLoader.IsBusy)
      {
         LogHelper.Warn("MatchMaking", "ReturnToPartyLobby: already in progress - ignoring the duplicate request.");
         return;
      }

      if (IsInMatch())
      {
         SceneLoader.Cover(ReturnToPartyLobbyNow, () => _returningToPartyLobby == false && IsMatchTeardownComplete());
         return;
      }

      ReturnToPartyLobbyNow();
   }

   private async void ReturnToPartyLobbyNow()
   {
      if (_returningToPartyLobby)
      {
         LogHelper.Warn("MatchMaking", "ReturnToPartyLobby: already in progress - ignoring the duplicate request.");
         return;
      }

      // Already behind the loading screen (if there is a match), so no second cover here.
      if (GameManager.Instance != null && GameManager.Instance.isPlayingOffline)
      {
         PerformLeaveMatch();
         return;
      }

      _returningToPartyLobby = true;
      _runnerStartRequested = false;

      if (QuantumRunner.Default != null)
         QuantumRunner.ShutdownAll();

      string partyRoomCode = PartyRoomCode;

      try
      {
         await Client.LeaveRoomAsync(becomeInactive: false);

         if (!string.IsNullOrEmpty(partyRoomCode))
            await Client.JoinOrCreateRoomAsync(BuildRoomArgs(partyRoomCode));
      }
      catch (Exception e)
      {
         LogHelper.Error("MatchMaking", $"ReturnToPartyLobby: failed to return to party room '{partyRoomCode}': {e}");
         AlertPopup.Show("Error", "Lost connection to the party.", () =>
         {
            GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
         });
         return;
      }
      finally
      {
         _returningToPartyLobby = false;
      }

      GameManager.Instance.MainMenuTab.windowManager.ShowWindow<MainMenuWindow>();
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
      SyncMatchRoom = 113,
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
      else if (photonEvent.Code == (byte)PhotonEventCode.SyncMatchRoom)
      {
         var data = (PhotonHashtable)photonEvent.CustomData;
         DesignatedLeaderUserId = (string)data["leader"];
         MoveToMatchRoomAsync((string)data["room"], (int)data["seed"]);
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

      // A disconnect with a match on screen and no cover already up (server eviction, client
      // timeout, plugin disconnect - anything that didn't come through LeaveMatch, which covers
      // itself) still gets the loading screen BEFORE the teardown below starts. A disconnect
      // that arrives while LeaveMatch's cover is up, or one that happens in the menu, runs directly.
      if (SceneLoader.IsBusy == false && IsInMatch())
      {
         SceneLoader.Cover(() => ReturnToMenuAfterDisconnect(cause), IsMatchTeardownComplete);
         return;
      }

      ReturnToMenuAfterDisconnect(cause);
   }

   // Set by InMatchWindow right before it disconnects on the player's behalf (plugin disconnect,
   // start timeout), so the alert explaining why can be shown once the menu Canvas is back up.
   private string _pendingDisconnectReason;

   public void SetPendingDisconnectReason(string reason)
   {
      _pendingDisconnectReason = reason;
   }

   private void ReturnToMenuAfterDisconnect(DisconnectCause cause)
   {
      // ShowWindow<MainMenuWindow>() has to run unconditionally, and BEFORE the alert below - it's
      // what actually recovers the screen (WindowManager.ShowWindow hides every other window, which
      // for InMatchWindow means Hide() re-enabling the menu Canvas it disabled in Show()). The
      // previous ordering deferred this until the player dismissed the AlertPopup, but AlertPopup's
      // PopupManager lives under that SAME Canvas - so for any disconnect that wasn't
      // DisconnectByClientLogic (e.g. the client timing out from being backgrounded/idle a while -
      // "client inactivity"), the popup asking the player to dismiss it was itself invisible and
      // unclickable, and the match screen just stayed up forever with the menu Canvas still off.
      var mainMenuTab = GameManager.Instance.MainMenuTab;
      mainMenuTab.windowManager.ShowWindow<MainMenuWindow>();

      // A pending reason means the disconnect was client-initiated (DisconnectByClientLogic) but not
      // chosen by the player - e.g. InMatchWindow reacting to a Quantum plugin disconnect.
      if (!string.IsNullOrEmpty(_pendingDisconnectReason))
         AlertPopup.Show("Disconnected", _pendingDisconnectReason);
      else if (cause != DisconnectCause.DisconnectByClientLogic)
         AlertPopup.Show("Disconnected", cause.ToString());
      _pendingDisconnectReason = null;

      if (QuantumRunner.Default != null)
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
