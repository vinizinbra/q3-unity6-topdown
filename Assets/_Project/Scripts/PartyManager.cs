using System;
using System.Collections.Generic;
using Photon.Client;
using Photon.Realtime;
using Quantum;
using QuantumUser.View.Util;
using UnityEngine;

public class PartyManager : PgSingleton<PartyManager>, IInRoomCallbacks, IMatchmakingCallbacks, IOnEventCallback
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
        SetPhase(PartyPhase.Connecting);
        MatchMakingConfig.Instance.Quickplay(roomCode);
    }

    public void LeaveParty()
    {
        MatchMakingConfig.Instance.CleanReconnectConfig();
        MatchMakingConfig.Instance.Client?.Disconnect();
        SetPhase(PartyPhase.JoinCreateChoice);
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
        MatchMakingConfig.Instance.StartQuantumGame();
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
    public void OnJoinedRoom() => HandleJoinedOrCreated();

    private void HandleJoinedOrCreated()
    {
        if (!IsPartyMatchType) return;

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

    public void OnLeftRoom()
    {
        _lastAllOthersReady = false;
        if (Phase != PartyPhase.JoinCreateChoice)
            SetPhase(PartyPhase.JoinCreateChoice);
    }

    public void OnPlayerEnteredRoom(Player newPlayer)
    {
        ToastManager.Instance?.Show($"{newPlayer.NickName} joined");
        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnPlayerLeftRoom(Player otherPlayer)
    {
        ToastManager.Instance?.Show($"{otherPlayer.NickName} left");
        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) { }

    public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnMasterClientSwitched(Player newMasterClient)
    {
        CheckAllReadyToast();
        OnRosterChanged?.Invoke();
    }

    public void OnEvent(EventData photonEvent) { }
}
