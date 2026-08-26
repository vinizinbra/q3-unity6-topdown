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

    private void Start()
    {
        PartyManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        PartyManager.Instance.OnRosterChanged += RefreshRoster;

        createButton.onClick.AddListener(CreateClicked);
        joinButton.onClick.AddListener(JoinClicked);
        leaveButton.onClick.AddListener(LeaveClicked);

        // Character selection isn't tied to being in a room - populate/wire the dropdown once,
        // up front, so a player can pick their character on the join/create panel too, not just
        // after joining. Previously this ran every time HandlePhaseChanged(InRoom) fired, which
        // also meant re-entering the room phase silently reset the pick back to index 0.
        InitializeCharacterDropdown();
        HandlePhaseChanged(PartyManager.Instance.Phase);
    }

    private void OnDestroy()
    {
        createButton.onClick.RemoveListener(CreateClicked);
        joinButton.onClick.RemoveListener(JoinClicked);
        leaveButton.onClick.RemoveListener(LeaveClicked);

        if (PartyManager.Instance == null) return;
        PartyManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        PartyManager.Instance.OnRosterChanged -= RefreshRoster;
    }

    private void Update()
    {
        if (connectingPanel.activeSelf)
            connectingText.text = MatchMakingConfig.Instance.Client.State.ToString();
    }

    private void CreateClicked() => PartyManager.Instance.CreateParty();
    private void JoinClicked() => PartyManager.Instance.JoinParty(PendingRoomCode);
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
            playerCountText.text = $"{room.PlayerCount}/{room.MaxPlayers}";
        }

        _remotePlayers.Clear();
        if (room != null && localPlayer != null)
        {
            foreach (var kv in room.Players)
            {
                if (kv.Value.ActorNumber != localPlayer.ActorNumber)
                    _remotePlayers.Add(kv.Value);
            }

            _remotePlayers.Sort((a, b) => a.ActorNumber.CompareTo(b.ActorNumber));
        }

        int slot = 0;

        if (localPlayer != null && slot < playerWidgets.Length)
        {
            // The character comes from PartyManager's own mirror rather than the Photon property,
            // which isn't written yet while there's no room to write it into - see LocalCharacterId.
            SetupSlot(slot, DisplayNameFor(localPlayer), localPlayer, PartyManager.Instance.LocalCharacterId);
            slot++;
        }

        for (int i = 0; i < _remotePlayers.Count && slot < playerWidgets.Length; i++, slot++)
        {
            var player = _remotePlayers[i];
            PartyManager.Instance.TryGetCharacterId(player, out var characterId);
            SetupSlot(slot, DisplayNameFor(player), player, characterId);
        }

        ClearSlotsFrom(slot);
    }

    private void SetupSlot(int index, string playerName, Player player, string characterId)
    {
        if (playerWidgets[index] == null)
            return;

        string characterDisplayName = null;
        var catalog = PartyManager.Instance.characterCatalog;
        if (catalog != null)
            catalog.TryGetDisplayName(characterId, out characterDisplayName);

        playerWidgets[index].Setup(playerName, PartyManager.Instance.TryGetReady(player), characterDisplayName, player.IsMasterClient, characterId);
    }

    // An empty name is a slot's "nobody here" signal, so a player who hasn't set one yet would
    // blank their own card out - which is reachable for the local player before connecting.
    private static string DisplayNameFor(Player player)
    {
        return string.IsNullOrEmpty(player.NickName) ? "You" : player.NickName;
    }

    // Leaving a party doesn't empty the whole roster any more - slot 0 is still you. Rebuild it
    // instead, which fills your own card and clears the teammate slots behind it.
    private void ClearRoster()
    {
        _remotePlayers.Clear();
        RefreshRoster();
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
