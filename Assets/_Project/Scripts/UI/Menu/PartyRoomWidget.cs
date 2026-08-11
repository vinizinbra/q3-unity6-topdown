using System.Linq;
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

    private bool _dropdownPopulated;

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
    private void JoinClicked() => PartyManager.Instance.JoinParty(roomCodeInput.text.Trim());
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

    private void RefreshRoster()
    {
        var room = MatchMakingConfig.Instance.Client.CurrentRoom;
        if (room == null) return;

        roomCodeText.text = room.Name;
        regionText.text = MatchMakingConfig.Instance.Client.CurrentRegion.ToUpper();
        playerCountText.text = $"{room.PlayerCount}/{room.MaxPlayers}";

        var catalog = PartyManager.Instance.characterCatalog;
        int i = 0;
        foreach (var kv in room.Players)
        {
            if (i >= playerWidgets.Length) break;
            var player = kv.Value;
            PartyManager.Instance.TryGetCharacterId(player, out var characterId);
            string characterDisplayName = null;
            if (catalog != null)
                catalog.TryGetDisplayName(characterId, out characterDisplayName);
            playerWidgets[i].Setup(player.NickName, PartyManager.Instance.TryGetReady(player), characterDisplayName, player.IsMasterClient);
            i++;
        }

        for (; i < playerWidgets.Length; i++)
            playerWidgets[i].Setup("", false);
    }
}
