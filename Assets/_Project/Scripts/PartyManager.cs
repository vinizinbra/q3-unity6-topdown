using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

public class PartyManager : PgSingleton<PartyManager>, IInRoomCallbacks, IMatchmakingCallbacks, IConnectionCallbacks, IOnEventCallback
{
    public static PartyManager Instance;

    public const string PropKeyCharacter = "character";
    public const string PropKeyReady = "ready";

    public CharacterCatalog characterCatalog;

    public enum PartyPhase
    {
        JoinCreateChoice,
        Connecting,
        InRoom
    }

    public PartyPhase Phase { get; private set; } = PartyPhase.JoinCreateChoice;
    public event Action<PartyPhase> OnPhaseChanged;
    public event Action OnRosterChanged;

    // The local player's current character pick, mirrored here as plain state alongside the Photon
    // custom property SetLocalCharacter writes. The property is the authoritative copy the rest of
    // the party reads; this exists so UI can read the LOCAL pick without a room existing at all -
    // character selection deliberately works before connecting (see PartyRoomWidget), and
    // LocalPlayer's properties aren't a reliable local mirror at that point.
    public string LocalCharacterId { get; private set; }

    // Fires whenever the local pick changes, so anything showing it (CharacterPreviewWidget) stays
    // in sync without being wired to whichever screen happened to change it.
    public event Action<string> OnLocalCharacterChanged;

    private bool _autoStartWhenRoomReady;
    private bool _lastAllOthersReady;

    // Set the moment this client sees SyncMatchRoom (see OnEvent below) - everyone in the party
    // room is about to leave it together to move into the fresh match room, so the OnPlayerLeftRoom
    // churn that follows for whichever clients transition first is expected, not someone actually
    // leaving the party. Cleared once this client itself lands in a room again (see OnJoinedRoom).
    private bool _suppressRosterToasts;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    private void Start()
    {
        MatchMakingConfig.Instance.Client.AddCallbackTarget(this);
    }

    // RECONNECT counts too - matchMakingType gets overwritten to RECONNECT for the reconnect flow
    // regardless of what kind of room is being rejoined, and a party room is the only kind of
    // reconnectable room this game currently has (quickplay is unused).
    private static bool IsPartyMatchType => MatchMakingConfig.Instance.matchMakingType == MatchMakingConfig.MatchMakingType.CUSTOM
        || MatchMakingConfig.Instance.matchMakingType == MatchMakingConfig.MatchMakingType.RECONNECT;

    public bool InParty => MatchMakingConfig.Instance.Client != null
        && MatchMakingConfig.Instance.Client.InRoom
        && IsPartyMatchType;

    public bool IsPartyLeader => !InParty || MatchMakingConfig.Instance.Client.LocalPlayer.IsMasterClient;

    public bool IsLocalReady => TryGetReady(MatchMakingConfig.Instance.Client.LocalPlayer);

    public void CreateParty()
    {
        var roomCode = UnityEngine.Random.Range(0, 99999).ToString("00000");
        BeginConnect(roomCode);
    }

    public void JoinParty(string roomCode)
    {
        BeginConnect(roomCode);
    }

    public void QuickStartSolo()
    {
        _autoStartWhenRoomReady = true;
        CreateParty();
    }

    private void BeginConnect(string roomCode)
    {
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.CUSTOM;
        // Remembered so ReturnToPartyLobby/MoveToMatchRoomAsync can tell the party's own room
        // apart from whatever single-use match room a run is currently played in - see
        // MatchMakingConfig.PartyRoomCode's own comment.
        MatchMakingConfig.Instance.PartyRoomCode = roomCode;
        SetPhase(PartyPhase.Connecting);
        MatchMakingConfig.Instance.Quickplay(roomCode);
    }

    // Deliberately a graceful room leave (LeaveRoomAsync, becomeInactive: false), not the old
    // Client.Disconnect(): a full peer disconnect leaves this actor marked INACTIVE in the room
    // (PlayerTtl > 0 here, for the reconnect feature) for up to 5 minutes instead of actually
    // removing it, and a re-connect right after (e.g. quickly Create -> Leave -> Create again)
    // reused the SAME underlying peer/UserId before that inactive reservation cleared - observed as
    // Photon.Realtime.OperationException: "Found inactive UserId '...', but not rejoining
    // (JoinMode=1)" (ErrorCode.JoinFailedFoundInactiveJoiner) on the next Connect(). A proper room
    // leave removes the actor immediately instead of leaving a stale reservation behind, and stays
    // connected to the master server the whole time - same pattern as ReturnToPartyLobby/
    // MoveToMatchRoomAsync.
    public async void LeaveParty()
    {
        MatchMakingConfig.Instance.CleanReconnectConfig();
        SetPhase(PartyPhase.JoinCreateChoice);

        var client = MatchMakingConfig.Instance.Client;
        if (client == null || !client.InRoom)
            return;

        try
        {
            await client.LeaveRoomAsync(becomeInactive: false);
        }
        catch (Exception e)
        {
            LogHelper.Error("MatchMaking", $"LeaveParty: LeaveRoomAsync failed: {e}");
        }
    }

    public void SetLocalCharacter(string characterId)
    {
        LogHelper.Error("CharacterSelect", $"SetLocalCharacter({characterId}) - writing custom property for local player {MatchMakingConfig.Instance.Client.LocalPlayer.ActorNumber}");
        MatchMakingConfig.Instance.Client.LocalPlayer.SetCustomProperties(new PhotonHashtable { { PropKeyCharacter, characterId } });

        LocalCharacterId = characterId;
        OnLocalCharacterChanged?.Invoke(characterId);
    }

    public void SetLocalReady(bool ready)
    {
        MatchMakingConfig.Instance.Client.LocalPlayer.SetCustomProperties(new PhotonHashtable { { PropKeyReady, ready } });
    }

    public void ToggleLocalReady()
    {
        SetLocalReady(!TryGetReady(MatchMakingConfig.Instance.Client.LocalPlayer));
    }

    public bool TryGetReady(Player player)
    {
        return player.CustomProperties.TryGetValue(PropKeyReady, out var value) && value is bool ready && ready;
    }

    public bool TryGetCharacterId(Player player, out string characterId)
    {
        if (player.CustomProperties.TryGetValue(PropKeyCharacter, out var value) && value is string id)
        {
            characterId = id;
            return true;
        }

        characterId = null;
        return false;
    }

    public bool AllOthersReady()
    {
        var room = MatchMakingConfig.Instance.Client.CurrentRoom;
        foreach (var kv in room.Players)
        {
            if (kv.Value.ActorNumber != room.MasterClientId && !TryGetReady(kv.Value))
                return false;
        }

        return true;
    }

    public void StartRun()
    {
        if (!IsPartyLeader) return;
        MatchMakingConfig.Instance.StartMatchInNewRoom();
    }

    private void CheckAllReadyToast()
    {
        bool allReady = InParty && IsPartyLeader && AllOthersReady();
        if (allReady && !_lastAllOthersReady)
            ToastManager.Instance?.Show("Everyone is ready!");
        _lastAllOthersReady = allReady;
    }

    // Applied uniformly to every local RuntimePlayer slot in StartRunner() - couch co-op means one
    // Photon client can have more than one, but character selection is only tracked per Photon
    // player, not per local slot.
    public AssetRef<EntityPrototype> ResolveLocalCharacterAvatar()
    {
        bool found = TryGetCharacterId(MatchMakingConfig.Instance.Client.LocalPlayer, out var characterId);

        if (characterCatalog == null)
        {
            LogHelper.Warn("CharacterSelect", "ResolveLocalCharacterAvatar - characterCatalog is unassigned on PartyManager, returning default avatar");
            return default;
        }

        AssetRef<EntityPrototype> avatar = characterCatalog.Resolve(characterId);
        LogHelper.Log("CharacterSelect", $"ResolveLocalCharacterAvatar - read back characterId='{characterId}' (found={found}) -> avatar={avatar.Id.Value}");
        return avatar;
    }

    private void SetPhase(PartyPhase phase)
    {
        Phase = phase;
        OnPhaseChanged?.Invoke(phase);
    }

    public void OnFriendListUpdate(List<FriendInfo> friendList) { }

    public void OnCreatedRoom() { }

    public void OnJoinedRoom()
    {
        _suppressRosterToasts = false;

        // Republish on EVERY join/rejoin - the party room AND the ephemeral match room
        // (MatchMakingConfig.MoveToMatchRoomAsync/ReturnToPartyLobby both do a genuine leave+join,
        // never a Photon rejoin, so each one creates a brand new actor with no custom properties
        // carried over from the last room). LocalCharacterId survives as plain local state (see its
        // own comment) specifically so this can restore it regardless of which room this is -
        // StartRunner's AddLocalPlayers needs this property set even when landing straight in the
        // match room, not just for the party lobby roster display.
        if (!string.IsNullOrEmpty(LocalCharacterId))
            SetLocalCharacter(LocalCharacterId);

        HandleJoinedOrCreated();
    }

    private void HandleJoinedOrCreated()
    {
        if (!IsPartyMatchType) return;
        // Landing in the ephemeral match room, not the party room - nothing here is party/lobby
        // UI's business (ready state, toasts, roster) since that room never shows this UI at all.
        if (!MatchMakingConfig.Instance.IsInPartyRoom) return;

        // Hands leadership back to whoever led the party into the match that just ended, if the
        // party room got destroyed/recreated (a long match empties it past EmptyRoomTtl) and
        // Photon's own default election gave it to whoever got back first instead. See its own
        // comment - a no-op for anyone except the one client whose UserId actually matches.
        MatchMakingConfig.Instance.ReclaimLeadershipIfNeeded();

        SetPhase(PartyPhase.InRoom);
        _lastAllOthersReady = false;
        SetLocalReady(false);
        ToastManager.Instance?.Show(
            MatchMakingConfig.Instance.matchMakingType == MatchMakingConfig.MatchMakingType.RECONNECT
                ? "Rejoined"
                : MatchMakingConfig.Instance.Client.CurrentRoom.PlayerCount == 1 ? "Party created" : "Joined party");
        OnRosterChanged?.Invoke();

        if (_autoStartWhenRoomReady)
        {
            _autoStartWhenRoomReady = false;
            StartRun();
        }
    }

    public void OnCreateRoomFailed(short returnCode, string message) => HandleJoinFailed(message);
    public void OnJoinRoomFailed(short returnCode, string message) => HandleJoinFailed(message);

    private void HandleJoinFailed(string message)
    {
        if (!IsPartyMatchType) return;

        _autoStartWhenRoomReady = false;
        ToastManager.Instance?.Show($"Join failed: {message}");
        SetPhase(PartyPhase.JoinCreateChoice);
    }

    public void OnJoinRandomFailed(short returnCode, string message) { }

    // Connection-level callbacks. The matchmaking failures above (OnJoinRoomFailed /
    // OnCreateRoomFailed) only cover failures the server reaches far enough to report - a room that
    // is full or missing. A connection that dies before that (network timeout, unreachable server,
    // auth/region failure) or a hard drop while already InRoom (kick, lost link) never fires those,
    // it fires OnDisconnected instead. Without listening for it the widget stays pinned on the
    // Connecting panel forever, since nothing ever moves Phase off Connecting. MatchMakingConfig
    // handles the popup + return-to-menu side; here we only need to unstick our own phase so the
    // party widget shows the join/create panel again the moment the menu reappears.
    public void OnDisconnected(DisconnectCause cause)
    {
        _autoStartWhenRoomReady = false;
        _lastAllOthersReady = false;
        if (Phase != PartyPhase.JoinCreateChoice)
            SetPhase(PartyPhase.JoinCreateChoice);
    }

    public void OnConnected() { }
    public void OnConnectedToMaster() { }
    public void OnRegionListReceived(RegionHandler regionHandler) { }
    public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
    public void OnCustomAuthenticationFailed(string debugMessage) { }

    public void OnLeftRoom()
    {
        _lastAllOthersReady = false;
        if (Phase != PartyPhase.JoinCreateChoice)
            SetPhase(PartyPhase.JoinCreateChoice);
    }

    public void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!MatchMakingConfig.Instance.IsInPartyRoom) return;

        ToastManager.Instance?.Show($"{newPlayer.NickName} joined");
        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!MatchMakingConfig.Instance.IsInPartyRoom) return;
        if (_suppressRosterToasts) return;

        ToastManager.Instance?.Show($"{otherPlayer.NickName} left");
        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) { }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (!MatchMakingConfig.Instance.IsInPartyRoom) return;

        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnMasterClientSwitched(Player newMasterClient)
    {
        if (!MatchMakingConfig.Instance.IsInPartyRoom) return;

        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == (byte)MatchMakingConfig.PhotonEventCode.SyncMatchRoom)
            _suppressRosterToasts = true;
    }
}
