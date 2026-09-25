using System.Collections.Generic;
using Photon.Deterministic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// End-of-run summary popup (Win/Lose) - registered with InMatchPopupManager like any other UiPopup,
// found by RunResultManager via InMatchPopupManager.Open<RunResultPopup>() and populated through
// Setup() right after. winRoot/loseRoot are toggled by which one actually happened; everything
// else (kill/damage/downed/Rift Shards, the optional team-damage breakdown) is shared. Both
// buttons do the same thing (MatchMakingConfig.ReturnToPartyLobby, NOT LeaveMatch - the run is
// already over here, so this keeps the party together in the lobby instead of disconnecting
// everyone) - see RunResultManager for why there are two identical actions.
public class RunResultPopup : UiPopup
{
    [Header("Win / Lose")]
    [SerializeField] private GameObject winRoot;
    [SerializeField] private GameObject loseRoot;
    [SerializeField] private TMP_Text clearTimeText;    // winRoot only - total time of the run
    [SerializeField] private TMP_Text timeSurvivedText; // loseRoot only - Global.RunTime

    [Header("Common stats")]
    [SerializeField] private TMP_Text enemiesKilledText;
    [SerializeField] private TMP_Text damageDealtText;
    [SerializeField] private TMP_Text timesDownedText;
    [SerializeField] private TMP_Text riftShardsText;

    [Header("Team Damage (co-op only)")]
    [SerializeField] private GameObject teamDamageRoot;
    [SerializeField] private Transform teamDamageContainer;
    [SerializeField] private TeamDamageWidget teamDamageWidgetPrefab;
    // The whole team's summed damage - the common damageDealtText above is THIS CLIENT's own
    // personal damage (see RunResultManager.ShowResult), never a team sum, so co-op needs this
    // separate text to show the aggregate the per-player breakdown rows are each a percentage of.
    [SerializeField] private TMP_Text teamDamageDealtText;

    [Header("Buttons")]
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private Button continueButton;

    private readonly List<TeamDamageWidget> _spawnedRows = new();

    public struct PlayerResult
    {
        public string HeroName;
        public Color HeroColor;
        public FP DamageDealt;
    }

    public override void Awake()
    {
        base.Awake();

        if (backToMenuButton != null)
            backToMenuButton.onClick.AddListener(OnLeaveClicked);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnLeaveClicked);
    }

    public void Setup(bool won, int enemiesKilled, FP damageDealt, int timesDowned, FP riftShardsEarned,
        FP runTime, IReadOnlyList<PlayerResult> teamResults)
    {
        if (winRoot != null)
            winRoot.SetActive(won);

        if (loseRoot != null)
            loseRoot.SetActive(won == false);

        if (won && clearTimeText != null)
            clearTimeText.text = FormatTime(runTime);

        if (won == false && timeSurvivedText != null)
            timeSurvivedText.text = FormatTime(runTime);

        if (enemiesKilledText != null)
            enemiesKilledText.text = enemiesKilled.ToString();

        if (damageDealtText != null)
            damageDealtText.text = TeamDamageWidget.FormatCompact(damageDealt.AsFloat);

        if (timesDownedText != null)
            timesDownedText.text = timesDowned.ToString();

        if (riftShardsText != null)
            riftShardsText.text = TeamDamageWidget.FormatCompact(riftShardsEarned.AsFloat);

        SetupTeamDamage(teamResults);
    }

    private void SetupTeamDamage(IReadOnlyList<PlayerResult> teamResults)
    {
        foreach (var row in _spawnedRows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }
        _spawnedRows.Clear();

        bool isTeam = teamResults != null && teamResults.Count > 1;

        if (teamDamageRoot != null)
            teamDamageRoot.SetActive(isTeam);

        if (isTeam == false || teamDamageContainer == null || teamDamageWidgetPrefab == null)
            return;

        FP teamTotal = FP._0;
        for (int i = 0; i < teamResults.Count; i++)
            teamTotal += teamResults[i].DamageDealt;

        if (teamDamageDealtText != null)
            teamDamageDealtText.text = TeamDamageWidget.FormatCompact(teamTotal.AsFloat);

        var sorted = new List<PlayerResult>(teamResults);
        sorted.Sort((a, b) => b.DamageDealt.AsFloat.CompareTo(a.DamageDealt.AsFloat));

        foreach (var result in sorted)
        {
            TeamDamageWidget row = Instantiate(teamDamageWidgetPrefab, teamDamageContainer);
            // Instantiate copies the source's active state - lets teamDamageWidgetPrefab be
            // authored as a normal, enabled child living right inside this same popup prefab (a
            // separate standalone prefab asset works too) rather than requiring it be manually set
            // inactive beforehand.
            row.gameObject.SetActive(true);
            row.Setup(result.HeroName, result.DamageDealt, teamTotal, result.HeroColor);
            _spawnedRows.Add(row);
        }

        // The template itself is never one of the real rows above - hide it now that every actual
        // player's clone has been spawned, so it doesn't sit there as a stray extra row.
        teamDamageWidgetPrefab.gameObject.SetActive(false);
    }

    private static string FormatTime(FP seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds.AsFloat));
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        return $"{minutes:00}:{secs:00}";
    }

    private void OnLeaveClicked()
    {
        MatchMakingConfig.Instance.ReturnToPartyLobby();
    }
}
