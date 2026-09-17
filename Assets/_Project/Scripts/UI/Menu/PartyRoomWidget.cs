using System.Collections.Generic;
using System.Linq;
using Photon.Realtime;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PartyRoomWidget : MonoBehaviour
{
    [Header("Panels")]
    public GameObject joinCreatePanel;
    public GameObject connectingPanel;
    public GameObject roomPanel;

    [Header("Join/Create panel")]
    public TMP_InputField roomCodeInput;
    public Button createButton;
    public Button joinButton;

    [Header("Connecting panel")]
    public TMP_Text connectingText;

    [Header("Room panel")]
    public TMP_Text roomCodeText;
    public TMP_Text regionText;
    public TMP_Text playerCountText;
    public RoomWidget[] playerWidgets;
    public TMP_Dropdown characterDropdown;
    public Button leaveButton;

    // The room code the player has typed but not acted on yet. Exposed so the main menu's single
    // Play button can offer to Join instead of starting a solo run while a code is sitting there
    // (see MainMenuWindow) - the two controls would otherwise disagree about what the player is
    // about to do.
    public string PendingRoomCode => roomCodeInput != null ? roomCodeInput.text.Trim() : string.Empty;

    public bool HasPendingRoomCode => string.IsNullOrEmpty(PendingRoomCode) == false;

    private bool _dropdownPopulated;

    // Reused across refreshes rather than allocated per call - RefreshRoster runs on every roster
    // and player-property change, which during a busy lobby is often.
    private readonly List<Player> _remotePlayers = new List<Player>();

    private bool _initialized;

    private void Start()
    {
        TryInitialize();
    }

    // PartyManager is a scene singleton that assigns Instance in its own Awake - same guarantee
    // every other PgSingleton here relies on - but if it's ever spawned/awoken after this
    // component's own Start (e.g. bootstrapped rather than a native scene object), wiring straight
    // into PartyManager.Instance.OnPhaseChanged here would NullReferenceException and silently
    // abort the REST of Start() too: every button listener below it would never get wired, and
    // slot 0 would never get its first population - permanently, since Start() only ever runs
    // once. Retrying every frame until PartyManager.Instance actually exists costs nothing once
    // steady-state (one null check) and self-heals instead of wedging the whole widget dead on a
    // one-frame race - same class of bug as this codebase's other one-shot init-order traps.
    private void TryInitialize()
    {
        if (_initialized || PartyManager.Instance == null)
            return;

        _initialized = true;

        PartyManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        PartyManager.Instance.OnRosterChanged += RefreshRoster;

        createButton.onClick.AddListener(CreateClicked);
        joinButton.onClick.AddListener(JoinClicked);
        leaveButton.onClick.AddListener(LeaveClicked);
        // TMP_InputField's own submit event (Enter/Return while the field is focused) - lets a
        // player type a code and hit Enter instead of having to reach for the Join button.
        roomCodeInput.onSubmit.AddListener(HandleRoomCodeSubmit);

        // Character selection isn't tied to being in a room - populate/wire the dropdown once,
        // up front, so a player can pick their character on the join/create panel too, not just
        // after joining. Previously this ran every time HandlePhaseChanged(InRoom) fired, which
        // also meant re-entering the room phase silently reset the pick back to index 0.
        InitializeCharacterDropdown();
        HandlePhaseChanged(PartyManager.Instance.Phase);
    }

    private void OnDestroy()
    {
        if (_initialized == false) return;

        createButton.onClick.RemoveListener(CreateClicked);
        joinButton.onClick.RemoveListener(JoinClicked);
        leaveButton.onClick.RemoveListener(LeaveClicked);
        roomCodeInput.onSubmit.RemoveListener(HandleRoomCodeSubmit);

        if (PartyManager.Instance == null) return;
        PartyManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        PartyManager.Instance.OnRosterChanged -= RefreshRoster;
    }

    private void Update()
    {
        if (_initialized == false)
        {
            TryInitialize();
            return;
        }

        if (connectingPanel.activeSelf)
            connectingText.text = MatchMakingConfig.Instance.Client.State.ToString();
    }

    // onSubmit still fires on Enter even with nothing typed - JoinClicked itself is what guards
    // against an empty field now, so this can just defer to it instead of duplicating the check.
    private void HandleRoomCodeSubmit(string _) => JoinClicked();

    private void CreateClicked() => PartyManager.Instance.CreateParty();

    private void JoinClicked()
    {
        if (HasPendingRoomCode == false)
        {
            ToastManager.Instance?.Show("Enter a room code to join.");
            return;
        }

        PartyManager.Instance.JoinParty(PendingRoomCode);
    }
    private void LeaveClicked() => PartyManager.Instance.LeaveParty();

    private void HandlePhaseChanged(PartyManager.PartyPhase phase)
    {
        LogHelper.Error("CharacterSelect", $"HandlePhaseChanged({phase}) - matchMakingType={MatchMakingConfig.Instance.matchMakingType}");
        joinCreatePanel.SetActive(phase == PartyManager.PartyPhase.JoinCreateChoice);
        connectingPanel.SetActive(phase == PartyManager.PartyPhase.Connecting);
        roomPanel.SetActive(phase == PartyManager.PartyPhase.InRoom);

        if (phase == PartyManager.PartyPhase.InRoom)
        {
            RefreshRoster();
        }
        else
        {
            // Left the party, was disconnected, or a join failed. Hiding roomPanel alone is not
            // enough: each slot's CharacterPreviewWidget keeps its instantiated hero alive on a
            // stage parked at the scene root, outside this panel's hierarchy, so without an
            // explicit clear the old party is still built in memory - and would flash back on
            // screen the moment the panel is shown again, before the next refresh replaces it.
            ClearRoster();
        }
    }

    // Called once from Start() - populates the dropdown, wires its listener, and defaults the
    // selection to index 0 if nothing's been picked yet. Not tied to phase, so a pick made before
    // creating/joining a room survives the transition into InRoom instead of being reset.
    private void InitializeCharacterDropdown()
    {
        var catalog = PartyManager.Instance.characterCatalog;
        if (catalog == null || catalog.characters.Length == 0 || _dropdownPopulated) return;

        characterDropdown.ClearOptions();
        characterDropdown.AddOptions(catalog.characters.Select(c => c.displayName).ToList());
        characterDropdown.onValueChanged.AddListener(OnCharacterDropdownChanged);
        _dropdownPopulated = true;

        characterDropdown.SetValueWithoutNotify(0);
        PartyManager.Instance.SetLocalCharacter(catalog.characters[0].id);
    }

    private void OnCharacterDropdownChanged(int index)
    {
        var catalog = PartyManager.Instance.characterCatalog;
        if (catalog == null || index < 0 || index >= catalog.characters.Length) return;
        LogHelper.Log("CharacterSelect", $"Dropdown changed to index {index} ({catalog.characters[index].id})");
        PartyManager.Instance.SetLocalCharacter(catalog.characters[index].id);
    }

    // Slot 0 is always the LOCAL player - their own card, whose portrait is the big main preview -
    // and the remaining slots are teammates ordered by ActorNumber. So joining a 3-player party as
    // player 2 puts you in slot 0, player 1 in slot 1 and player 3 in slot 2.
    //
    // Your own slot is filled even with no party open, so the main menu shows your card while solo
    // rather than an empty box you only populate by creating a room.
    //
    // Teammates are ordered by ActorNumber rather than by Room.Players' own enumeration, which is a
    // Dictionary and guarantees no ordering: without this a slot could silently swap which teammate
    // it shows between two refreshes, which reads as party members jumping around the roster.
    private void RefreshRoster()
    {
        var client = MatchMakingConfig.Instance != null ? MatchMakingConfig.Instance.Client : null;
        var localPlayer = client != null ? client.LocalPlayer : null;
        var room = client != null ? client.CurrentRoom : null;

        if (room != null)
        {
            roomCodeText.text = room.Name;
            regionText.text = client.CurrentRegion.ToUpper();
            // room.PlayerCount is a raw Players.Count - same PlayerTtl inactive-actor issue as the
            // roster below, so it can't be used directly here either without showing a stale total.
            int activeCount = room.Players.Count(kv => kv.Value.IsInactive == false);
            playerCountText.text = $"{activeCount}/{room.MaxPlayers}";
        }

        _remotePlayers.Clear();
        if (room != null && localPlayer != null)
        {
            foreach (var kv in room.Players)
            {
                // Rooms here run with a PlayerTtl (see MatchMakingConfig) so a reconnect can resume
                // a dropped session - which means a player who disconnects or explicitly leaves
                // isn't actually removed from Room.Players, just flagged IsInactive, for the whole
                // TTL window. Without this check their row/character preview stayed put looking
                // connected until that TTL finally expired server-side, regardless of whether they
                // were ever coming back.
                if (kv.Value.ActorNumber != localPlayer.ActorNumber && kv.Value.IsInactive == false)
                    _remotePlayers.Add(kv.Value);
            }

            _remotePlayers.Sort((a, b) => a.ActorNumber.CompareTo(b.ActorNumber));
        }

        int slot = SetupLocalSlot(localPlayer);

        for (int i = 0; i < _remotePlayers.Count && slot < playerWidgets.Length; i++, slot++)
        {
            var player = _remotePlayers[i];
            PartyManager.Instance.TryGetCharacterId(player, out var characterId);
            SetupSlot(slot, DisplayNameFor(player), player, characterId);
        }

        ClearSlotsFrom(slot);
    }

    // Fills slot 0 with your own card - never leaves it in the "nobody here" placeholder state
    // every other slot can fall into, because unlike a teammate slot, this ONE doubles as the main
    // menu's representation of your chosen hero: it must always show SOME hero, connected or not,
    // in a room or not, even mid-race before Client.LocalPlayer is available (see TryInitialize's
    // own comment on that race). Falls back to "You" and whatever LocalCharacterId/the catalog
    // resolve to (CharacterPreviewWidget/RoomWidget both already fall back to the first catalog
    // entry on their own) rather than passing through the same empty-name "nobody here" signal
    // ClearSlotsFrom uses for every other slot. Shared by RefreshRoster and ClearRoster (see its
    // own comment for why ClearRoster needs this instead of just calling RefreshRoster). Always
    // returns 1 (slot 0 is always considered filled) unless there's nothing to fill it with at all.
    private int SetupLocalSlot(Player localPlayer)
    {
        if (playerWidgets.Length == 0)
            return 0;

        // The character comes from PartyManager's own mirror rather than the Photon property,
        // which isn't written yet while there's no room to write it into - see LocalCharacterId.
        string playerName = localPlayer != null ? DisplayNameFor(localPlayer) : "You";
        SetupSlot(0, playerName, localPlayer, PartyManager.Instance.LocalCharacterId);
        return 1;
    }

    // player is null only for slot 0 before Client.LocalPlayer is available (see SetupLocalSlot) -
    // ready/leader have no meaning yet at that point, so they just read as false rather than the
    // whole slot bailing out and reverting to placeholder.
    private void SetupSlot(int index, string playerName, Player player, string characterId)
    {
        if (playerWidgets[index] == null)
            return;

        string characterDisplayName = null;
        var catalog = PartyManager.Instance.characterCatalog;
        if (catalog != null)
            catalog.TryGetDisplayName(characterId, out characterDisplayName);

        bool isReady = player != null && PartyManager.Instance.TryGetReady(player);
        bool isLeader = player != null && player.IsMasterClient;
        playerWidgets[index].Setup(playerName, isReady, characterDisplayName, isLeader, characterId);
    }

    // An empty name is a slot's "nobody here" signal, so a player who hasn't set one yet would
    // blank their own card out - which is reachable for the local player before connecting.
    private static string DisplayNameFor(Player player)
    {
        return string.IsNullOrEmpty(player.NickName) ? "You" : player.NickName;
    }

    // Deliberately does NOT call RefreshRoster/read Client.CurrentRoom's Players for the teammate
    // slots - Disconnect()/LeaveRoom() is async, so right after LeaveParty() fires this,
    // CurrentRoom can still briefly be the OLD room with every old party mate still marked active
    // (nobody else's IsInactive flips just because I'm the one leaving), which would just
    // repopulate the exact roster this is meant to clear. And because this phase change already
    // flips Phase to JoinCreateChoice, PartyManager's later, ACCURATE OnLeftRoom/OnDisconnected
    // never gets a second chance to retrigger it (Phase is already what it's guarding for). So:
    // once we're leaving InRoom, there are no teammates from the UI's perspective, full stop -
    // wipe every teammate slot, including its CharacterPreviewWidget, which parks its instantiated
    // hero at its own stage outside this panel's hierarchy and would otherwise go on rendering
    // former party mates in the 3D scene even with roomPanel hidden. Slot 0 still goes through
    // SetupLocalSlot though, same as RefreshRoster - Client.LocalPlayer isn't room state, so it's
    // not subject to the same staleness, and clearing it too would blank out the main menu's own
    // "you, solo" card the moment the game opens (Phase starts at JoinCreateChoice).
    private void ClearRoster()
    {
        _remotePlayers.Clear();

        var localPlayer = MatchMakingConfig.Instance != null ? MatchMakingConfig.Instance.Client?.LocalPlayer : null;
        ClearSlotsFrom(SetupLocalSlot(localPlayer));
    }

    // Setup with an empty name is a slot's "nobody here" state - it swaps to the inactive visual
    // and clears that slot's character preview.
    private void ClearSlotsFrom(int index)
    {
        for (int i = index; i < playerWidgets.Length; i++)
        {
            if (playerWidgets[i] != null)
                playerWidgets[i].Setup("", false);
        }
    }
}
