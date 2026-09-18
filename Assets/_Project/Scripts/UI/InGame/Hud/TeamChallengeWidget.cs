using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible, whole-team HUD banner for a Starting/ChallengeActive Optional Team Challenge -
// same idiom TraversalChallengeWidget/BreathingWidget already use for their own banners.
// Shown only while Global.CurrentState == GameState.TeamChallenge (resolved once a tick by
// CombatDirectorSystem.ApplyEffectiveState via TeamChallengeUtility.AnyBannerActive), which is what
// keeps this mutually exclusive with SurvivalWidget/BossWidget/TraversalChallengeWidget.
// Extends GameStateGatedWidget, reacting to the real GameStateChanged event instead of polling.
public class TeamChallengeWidget : GameStateGatedWidget
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField, Tooltip("The same one-line rules blurb InteractionPromptWidget's own Available-state prompt shows before activation (ChallengeDefinition.Description, e.g. \"Kill 20 enemies before the timer runs out\") - shown here too for the rest of the team, who never saw that prompt themselves. Optional - left unassigned, this field is simply skipped.")]
    private TMP_Text descriptionText;
    [SerializeField, Tooltip("Dedicated 3/2/1 pre-start countdown, separate from objectiveText (which is reused for the kill-count/timer readout once the challenge is actually active) so punching this one doesn't also punch on every kill/second tick during the real objective.")]
    private TMP_Text countdownText;

    [Header("Punch on countdown change")]
    [SerializeField, Tooltip("Punched whenever the displayed countdown number changes - defaults to countdownText's own transform if left unassigned.")]
    private Transform countdownPunchTarget;
    [SerializeField] private Vector3 countdownPunchStrength = new Vector3(0.35f, 0.35f, 0f);
    [SerializeField] private float countdownPunchDuration = 0.3f;
    [SerializeField] private float countdownPunchFrequency = 12f;

    private int? _lastCountdownSeconds;
    private Tween _countdownPunchTween;
    private Vector3 _countdownRestScale;

    protected override void Awake()
    {
        base.Awake();
        QuantumEvent.Subscribe<EventTeamChallengeActivated>(this, OnActivated);
        QuantumEvent.Subscribe<EventTeamChallengeStarted>(this, OnStarted);
        QuantumEvent.Subscribe<EventTeamChallengeCompleted>(this, OnCompleted);
        QuantumEvent.Subscribe<EventTeamChallengeFailed>(this, OnFailed);

        Transform punchTransform = countdownPunchTarget != null ? countdownPunchTarget : (countdownText != null ? countdownText.transform : null);
        if (punchTransform != null)
            _countdownRestScale = punchTransform.localScale;
    }

    // Fires exactly once, on the Available->WaitingForTeam edge (see TeamChallengeUtility.
    // TryReadyUp) - unlike TraversalChallengeWidget's own unconditional whole-team toast, the
    // activator themselves needs a DIFFERENT message ("waiting for the rest of the team") than
    // everyone else ("[HeroName] activated a Rift Challenge") - same local-player filter idiom
    // InteractionPromptWidget.OnContextInteractionRejected already uses for "is this press mine".
    private unsafe void OnActivated(EventTeamChallengeActivated e)
    {
        if (IsMyLocalPlayer(e.Player) == true)
        {
            // A solo player (no other connected, non-incapacitated Raider - bots auto-ready too,
            // see TeamChallengeUtility.AutoReadyBots) is ALREADY the whole team the instant they
            // press Ready - TryBeginStarting (ticked every frame) moves straight to Starting with
            // nothing left to wait on. No toast needed there anymore ("Challenge starting..." read
            // as noisy - the STARTING countdown/CHALLENGE STARTED banner already cover it a moment
            // later); only the genuinely-still-waiting case gets one.
            if (ResolveAlreadyWholeTeam(e.Poi) == false)
                ToastManager.Instance?.Show("Waiting for other Raiders to activate.");

            return;
        }

        string heroName = "A Raider";

        if (_game != null)
        {
            Frame frame = _game.Frames.Predicted;

            if (frame.Unsafe.TryGetPointer<CharacterStats>(e.Player, out var stats) == true
                && frame.Unsafe.TryGetPointer<PlayerLink>(e.Player, out var playerLink) == true)
            {
                heroName = RunResultManager.ResolvePlayerLabel(playerLink->Player, stats->CharacterData);
            }
        }

        ToastManager.Instance?.Show($"{heroName} activated a Rift Challenge.");
    }

    // Fires once the Starting countdown ends and the objective actually begins - the big
    // "CHALLENGE STARTED" banner, distinct from OnActivated's own toast (which fired much earlier,
    // on the first Ready press). CurrentState is already TeamChallenge by this point, so
    // _bannerPlaying isn't strictly needed for THIS banner to show, but it's set anyway for
    // symmetry with OnCompleted/OnFailed and in case the announcement outlives the state somehow.
    private void OnStarted(EventTeamChallengeStarted e)
    {
        AnnouncerManager.Instance?.Announce("CHALLENGE STARTED", OnBannerComplete);
    }

    private void OnCompleted(EventTeamChallengeCompleted e)
    {
        AnnouncerManager.Instance?.Announce("CHALLENGE COMPLETE", OnBannerComplete);
    }

    private void OnFailed(EventTeamChallengeFailed e)
    {
        AnnouncerManager.Instance?.Announce("CHALLENGE FAILED", OnBannerComplete);
    }

    private void OnBannerComplete()
    {
        RequestRecompute();
    }

    private void PlayCountdownPunch()
    {
        Transform target = countdownPunchTarget != null ? countdownPunchTarget : (countdownText != null ? countdownText.transform : null);
        if (target == null)
            return;

        // Reset to the authored rest scale before punching again - Tween.PunchScale punches
        // relative to whatever scale is CURRENT when it's called, so without this a punch that
        // lands before the previous one finished decaying compounds on top of it instead of
        // punching from rest (see CurrencyUiWidget.PlayPunch for the same reset-before-punch idiom).
        _countdownPunchTween.Stop();
        target.localScale = _countdownRestScale;
        _countdownPunchTween = Tween.PunchScale(target, countdownPunchStrength, countdownPunchDuration, countdownPunchFrequency, useUnscaledTime: true);
    }

    // TryBeginStarting (TeamChallengeUtility) is ticked every frame from TeamChallengeSystem and can
    // run the SAME tick a solo/last Raider readies up - by the time this event reaches the View, the
    // just-added TeamChallengeReady marker may already have been converted into a
    // TeamChallengeParticipant (ConvertReadyToParticipants) and the challenge's own State advanced
    // past WaitingForTeam. Re-counting TeamChallengeReady markers alone (what this used to do) reads
    // 0 in that case - checking the challenge's own State first is what makes this correct here too:
    // anything past WaitingForTeam means unanimity was already reached, full stop.
    private unsafe bool ResolveAlreadyWholeTeam(EntityRef poi)
    {
        if (_game == null)
            return false;

        Frame frame = _game.Frames.Predicted;

        if (frame.Unsafe.TryGetPointer<TeamChallenge>(poi, out var challenge) == false)
            return false;

        if (challenge->State != TeamChallengeState.WaitingForTeam)
            return true;

        return TeamChallengeUtility.CountReady(frame, poi) >= TeamChallengeUtility.CountRequiredRaiders(frame);
    }

    private static bool IsMyLocalPlayer(EntityRef player)
    {
        if (MyLocalPlayer.Instance == null)
            return false;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet && slots[i].EntityRef == player)
                return true;
        }

        return false;
    }

    protected override bool IsActive(GameState currentGameState)
    {
        return currentGameState == GameState.TeamChallenge ;
    }

    protected override unsafe void OnActiveQUpdate(Frame frame)
    {
        if (frame.Global->CurrentState != GameState.TeamChallenge)
            return;
        var filtered = frame.Filter<TeamChallenge>();

        while (filtered.Next(out EntityRef _, out TeamChallenge challenge))
        {
            if (challenge.State != TeamChallengeState.Starting && challenge.State != TeamChallengeState.ChallengeActive)
                continue;

            ApplyObjective(frame, challenge);
            return;
        }
    }

    private void ApplyObjective(Frame frame, in TeamChallenge challenge)
    {
        ChallengeDefinition definition = frame.FindAsset(challenge.SelectedChallenge);

        if (descriptionText != null)
            descriptionText.text = definition != null ? definition.Description : string.Empty;

        if (challenge.State == TeamChallengeState.Starting)
        {
            if (titleText != null)
                titleText.text = "STARTING";

            if (countdownText != null)
            {
                int seconds = Mathf.CeilToInt(Mathf.Max(challenge.RemainingCountdown.AsFloat, 0f));

                // Disabled rather than shown as "0" - RemainingCountdown crossing to <= 0 flips
                // the challenge straight to ChallengeActive the same tick (TeamChallengeSystem.
                // TickStarting), so this is mostly a defensive guard (prediction/rollback could
                // still transiently read a State-Starting frame with a hit-zero countdown), but
                // it also means the counter never has to visually settle on "0" for a frame.
                bool shown = seconds > 0;

                if (countdownText.gameObject.activeSelf != shown)
                    countdownText.gameObject.SetActive(shown);

                if (shown)
                {
                    countdownText.text = seconds.ToString();

                    // Skip the very first read (_lastCountdownSeconds starts unset) so the countdown's
                    // first frame on screen doesn't punch - only an actual 3->2->1 change should.
                    if (_lastCountdownSeconds.HasValue && seconds != _lastCountdownSeconds.Value)
                        PlayCountdownPunch();
                }

                _lastCountdownSeconds = seconds;
            }

            return;
        }

        // Reset so the next Starting window (a future challenge) punches correctly instead of
        // silently skipping its own first frame because this still holds a stale prior value.
        _lastCountdownSeconds = null;

        // Starting is the only branch that ever shows/updates countdownText - once the challenge
        // moves on (ChallengeActive/RewardAvailable/Completed/Failed) nothing above touches it
        // again, so without this it would stay stuck on-screen showing its last digit (e.g. "1")
        // for the rest of the attempt. RemainingCountdown crossing to <= 0 flips State to
        // ChallengeActive the SAME tick (TeamChallengeSystem.TickStarting), so the seconds == 0
        // guard above almost never actually fires - this is the real, always-reachable disable path.
        if (countdownText != null && countdownText.gameObject.activeSelf)
            countdownText.gameObject.SetActive(false);

        if (titleText != null)
            titleText.text = definition != null ? definition.DisplayName : "TEAM CHALLENGE";

        if (objectiveText == null)
            return;

        if (definition != null && definition.Type == ChallengeType.CursedSurvival)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(challenge.RemainingChallengeTime.AsFloat, 0f));
            objectiveText.text = $"{seconds}s";
        }
        else
        {
            string timeSuffix = definition != null && definition.Duration.AsFloat > 0f
                ? $"  {Mathf.CeilToInt(Mathf.Max(challenge.RemainingChallengeTime.AsFloat, 0f))}s"
                : string.Empty;

            objectiveText.text = $"{challenge.KillCount} / {challenge.KillTarget}{timeSuffix}";
        }
    }
}
