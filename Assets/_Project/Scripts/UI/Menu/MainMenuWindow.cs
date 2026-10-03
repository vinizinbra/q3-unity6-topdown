using System;
using System.Collections;
using System.Collections.Generic;
using Playtime.Core;
using Quantum;
using Quantum.Demo;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using Button = UnityEngine.UI.Button;

public class MainMenuWindow : UiWindow
{
    [FormerlySerializedAs("matchMakingConfigNew")] public MatchMakingConfig matchMakingConfig;

    [Header("Buttons")]
    public Button playButton;
    public Button quickPlayButton;
    public Button practiceButton;

    [Tooltip("Shown only while there is a live match to rejoin (MatchMakingConfig.CanReconnect). Hidden entirely otherwise - it is not a disabled-but-visible control.")]
    public Button reconnectButton;

    // Two mutually-exclusive states (see PlayButtonClicked/Update below). Reconnect is no longer
    // one of them - it has its own button now, so Play never silently turns into something else.
    public TMP_Text playButtonLabel;

    [Tooltip("Optional - the party panel that owns the room-code field. While a code is typed there and this player isn't already in a party, the Play button becomes Join and joins that room instead of starting a run. Left unassigned, Play behaves exactly as before.")]
    public PartyRoomWidget partyRoom;

    [Header("World")]
    [Tooltip("Optional - one entry per WorldCatalog world. Writes the chosen WorldDefinition onto MatchMakingConfig.RuntimeConfig (World, SurvivalConfig, WorldBalance); in a party only the leader's pick is used (carried in the match-start event), so it's read-only for everyone else.")]
    public TMP_Dropdown worldDropdown;

    // WorldCatalog index. Remembered across sessions.
    private static readonly PlayerPrefInt WorldIndexPref = new PlayerPrefInt("menu_world_index", 0);

    [Header("Difficulty")]
    [Tooltip("Optional - Easy/Medium/Hard/Nightmare 1..maxNightmareLevel. Writes MatchMakingConfig.RuntimeConfig.Difficulty/NightmareLevel; in a party only the leader's pick is used (carried in the match-start event), so it's read-only for everyone else.")]
    public TMP_Dropdown difficultyDropdown;
    public int maxNightmareLevel = 10;

    // Dropdown index, not tier - Nightmare N is index 2 + N. Remembered across sessions.
    private static readonly PlayerPrefInt DifficultyIndexPref = new PlayerPrefInt("menu_difficulty_index", (int)DifficultyTier.Medium);

    private void Start()
    {
        playButton.onClick.AddListener(PlayButtonClicked);

        if (reconnectButton != null)
            reconnectButton.onClick.AddListener(Reconnect);

        if (practiceButton != null)
            practiceButton.onClick.AddListener(Practice);
       /* quickPlayButton.onClick.AddListener(QuickPlay);*/

        InitializeWorldDropdown();
        InitializeDifficultyDropdown();
    }

    private void OnDestroy()
    {
        playButton.onClick.RemoveListener(PlayButtonClicked);

        if (reconnectButton != null)
            reconnectButton.onClick.RemoveListener(Reconnect);

        if (practiceButton != null)
            practiceButton.onClick.RemoveListener(Practice);
       /* quickPlayButton.onClick.RemoveListener(QuickPlay);*/

        if (difficultyDropdown != null)
            difficultyDropdown.onValueChanged.RemoveListener(OnDifficultyChanged);

        if (worldDropdown != null)
            worldDropdown.onValueChanged.RemoveListener(OnWorldChanged);
    }

    private void InitializeWorldDropdown()
    {
        WorldCatalog catalog = WorldCatalog.Instance;

        if (catalog == null || catalog.Worlds.Count == 0)
        {
            LogHelper.Warn("MainMenu", "No WorldCatalog at Resources/Worlds/WorldCatalog - matches use whatever World/SurvivalConfig the RuntimeConfig was authored with.");
            return;
        }

        int index = Mathf.Clamp(WorldIndexPref.Value, 0, catalog.Worlds.Count - 1);

        if (worldDropdown != null)
        {
            var options = new List<string>();
            for (int i = 0; i < catalog.Worlds.Count; i++)
                options.Add(catalog.Worlds[i] != null && string.IsNullOrEmpty(catalog.Worlds[i].DisplayName) == false ? catalog.Worlds[i].DisplayName : $"World {i + 1}");

            worldDropdown.ClearOptions();
            worldDropdown.AddOptions(options);
            worldDropdown.onValueChanged.AddListener(OnWorldChanged);
            worldDropdown.SetValueWithoutNotify(index);
        }

        // Applied even with no dropdown, so the remembered/default world still drives the match.
        MatchMakingConfig.ApplyWorld(matchMakingConfig != null ? matchMakingConfig.RuntimeConfig : MatchMakingConfig.Instance.RuntimeConfig, index);
    }

    private void OnWorldChanged(int index)
    {
        WorldIndexPref.Value = index;
        MatchMakingConfig.ApplyWorld(matchMakingConfig != null ? matchMakingConfig.RuntimeConfig : MatchMakingConfig.Instance.RuntimeConfig, index);
    }

    private void InitializeDifficultyDropdown()
    {
        if (difficultyDropdown == null)
            return;

        var options = new List<string> { "Easy", "Medium", "Hard" };
        for (int level = 1; level <= maxNightmareLevel; level++)
            options.Add(DifficultyConfig.GetDisplayName(DifficultyTier.Nightmare, level));

        difficultyDropdown.ClearOptions();
        difficultyDropdown.AddOptions(options);
        difficultyDropdown.onValueChanged.AddListener(OnDifficultyChanged);

        int index = Mathf.Clamp(DifficultyIndexPref.Value, 0, options.Count - 1);
        difficultyDropdown.SetValueWithoutNotify(index);
        ApplyDifficulty(index);
    }

    private void OnDifficultyChanged(int index)
    {
        DifficultyIndexPref.Value = index;
        ApplyDifficulty(index);
    }

    // Written straight onto the RuntimeConfig StartRunner/StartOfflineRunner clone, so it covers
    // solo, party and offline starts alike.
    private void ApplyDifficulty(int index)
    {
        var config = matchMakingConfig != null ? matchMakingConfig.RuntimeConfig : MatchMakingConfig.Instance.RuntimeConfig;
        if (index <= (int)DifficultyTier.Hard)
        {
            config.Difficulty = (DifficultyTier)index;
        }
        else
        {
            config.Difficulty = DifficultyTier.Nightmare;
            config.NightmareLevel = index - (int)DifficultyTier.Hard;
        }
    }

    public override void Show()
    {
        base.Show();
        Application.targetFrameRate = 120;
    }

    void Update()
    {
        // CanReconnect is already the full "is there anything to rejoin" answer - not in a room,
        // reconnect information present, and not timed out. See MatchMakingConfig.
        if (reconnectButton != null)
        {
            bool canReconnect = MatchMakingConfig.Instance.CanReconnect;
            if (reconnectButton.gameObject.activeSelf != canReconnect)
                reconnectButton.gameObject.SetActive(canReconnect);
        }

        // Only the leader's pick is sent with the match start, so a teammate's dropdown would be a
        // control that silently does nothing.
        bool isLeader = PartyManager.Instance.InParty == false || PartyManager.Instance.IsPartyLeader;

        if (difficultyDropdown != null && difficultyDropdown.interactable != isLeader)
            difficultyDropdown.interactable = isLeader;

        if (worldDropdown != null && worldDropdown.interactable != isLeader)
            worldDropdown.interactable = isLeader;

        if (ShouldOfferJoin)
            playButtonLabel.text = "Join";
        else if (PartyManager.Instance.IsPartyLeader)
            playButtonLabel.text = "Play";
        else
            playButtonLabel.text = PartyManager.Instance.IsLocalReady ? "Not Ready" : "Ready";
    }

    // A room code typed but not yet acted on means the player is clearly trying to join someone,
    // so the single prominent button should do that rather than drop them into a solo run they'd
    // have to back out of. Gated on not already being in a party: once in one, the code field is
    // behind the room panel and Play has to go back to being Play/Ready.
    private bool ShouldOfferJoin => partyRoom != null
        && partyRoom.HasPendingRoomCode
        && PartyManager.Instance.InParty == false;

    private void QuickPlay()
    {
        MatchMakingConfig.Instance.CleanReconnectConfig();
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.QUICKPLAY;
        MatchMakingConfig.Instance.Quickplay();
    }

    private void Reconnect()
    {
        MatchMakingConfig.Instance.matchMakingType = MatchMakingConfig.MatchMakingType.RECONNECT;
        MatchMakingConfig.Instance.ReconnectAsync();
    }

    // The Play/Ready button's click handler - see the label logic in Update() above for what's
    // currently shown. Either starts the run (leader/solo) or just toggles this player's own ready
    // state (non-leader party member). Reconnect is reconnectButton's job, not this one's.
    private void PlayButtonClicked()
    {
        // Checked before the solo path below, since that path is exactly what this replaces.
        if (ShouldOfferJoin)
        {
            PartyManager.Instance.JoinParty(partyRoom.PendingRoomCode);
            return;
        }

        if (!PartyManager.Instance.InParty)
        {
            // Show the loading screen immediately on click, not just once StartRunner() eventually
            // runs after the room-create + StartGame event round-trip - that gap is otherwise an
            // unresponsive-looking party screen. LoadingWindow, not ConnectingWindow: StartRun/
            // QuickStartSolo now genuinely leaves the party room to move into a fresh match room
            // (see MatchMakingConfig.MoveToMatchRoomAsync) - ConnectingWindow's IMatchmakingCallbacks.
            // OnLeftRoom treats ANY room leave as an unexpected disconnect ("Left the room
            // unexpectedly") and would tear the connection down over our own intentional leave.
            // LoadingWindow doesn't register room callbacks, and StartRunner shows it again anyway
            // once the match room is actually joined, so re-showing it here is harmless.
            GameManager.Instance.MainMenuTab.windowManager.ShowWindow<LoadingWindow>();
            PartyManager.Instance.QuickStartSolo();
            return;
        }

        if (PartyManager.Instance.IsPartyLeader)
        {
            if (PartyManager.Instance.AllOthersReady())
            {
                // Same reasoning as above (LoadingWindow, not ConnectingWindow) - the leader's own
                // StartRun call leaves the party room too, which ConnectingWindow would misreport.
                GameManager.Instance.MainMenuTab.windowManager.ShowWindow<LoadingWindow>();
                PartyManager.Instance.StartRun();
            }
            else
            {
                ToastManager.Instance?.Show("Waiting for everyone to be ready...");
            }
        }
        else
        {
            PartyManager.Instance.ToggleLocalReady();
        }
    }

    private void Practice()
    {
        // Offline mode starts a whole separate local session (MatchMakingConfig.StartOfflineRunner)
        // alongside the still-live party room connection rather than replacing it - the party's
        // reconnect state, ready flags, etc. keep sitting there and the click looked like it just
        // silently did nothing. Leaving the party first is what LeaveParty already does cleanly, so
        // point the player at it instead.
        if (PartyManager.Instance.InParty)
        {
            ToastManager.Instance?.Show("Leave your party before playing offline.");
            return;
        }

        GameManager.Instance.PlayOffline();
    }
}
