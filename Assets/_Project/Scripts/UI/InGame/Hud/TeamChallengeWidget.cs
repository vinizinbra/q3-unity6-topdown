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
    [SerializeField, Tooltip("Dedicated 3/2/1 pre-start countdown (slam-in digit, shockwave, screen-edge flash, tick sound) - its own widget, separate from objectiveText (which is reused for the kill-count/timer readout once the challenge is actually active). Placed in the upper third of the screen, never the centre, where the local hero stands.")]
    private ChallengeCountdownWidget countdown;

    protected override void Awake()
    {
        base.Awake();
        QuantumEvent.Subscribe<EventTeamChallengeActivated>(this, OnActivated);
        QuantumEvent.Subscribe<EventTeamChallengeReadyCanceled>(this, OnReadyCanceled);
        QuantumEvent.Subscribe<EventTeamChallengeStarted>(this, OnStarted);
        QuantumEvent.Subscribe<EventTeamChallengeCompleted>(this, OnCompleted);
        QuantumEvent.Subscribe<EventTeamChallengeFailed>(this, OnFailed);
        QuantumEvent.Subscribe<EventTeamChallengeRecharged>(this, OnRecharged);

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
            // A solo player (no other connected, non-incapacitated Raider - bots must walk over and
            // Ready themselves, see BotPoiUtility) is ALREADY the whole team the instant they
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

    // A failed challenge with a retry left is Available again (see TeamChallengeSystem.TickRecharging).
    private void OnRecharged(EventTeamChallengeRecharged e)
    {
        ToastManager.Instance?.Show("Rift Challenge recharged - ready to retry.");
    }

        private void OnBannerComplete()
    {
        RequestRecompute();
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

    private void OnReadyCanceled(EventTeamChallengeReadyCanceled e)
    {
        if (IsMyLocalPlayer(e.Player) == true)
            ToastManager.Instance?.Show("Team Challenge canceled - too far away");
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

    // Covers the widget being hidden mid-Starting (state change/reconnect) - otherwise the
    // countdown's tick-sound latch would carry over and silence the next challenge's first digits.
    protected override void OnShownChanged(bool shown, GameState currentGameState)
    {
        if (shown == false && countdown != null)
            countdown.Hide();
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

        // KillTarget is only committed at ChallengeActive (BeginChallengeActive) - during the
        // Starting countdown show the same live preview the POI prompt's rules row uses.
        if (descriptionText != null)
        {
            int killTarget = challenge.State == TeamChallengeState.ChallengeActive || definition == null
                ? challenge.KillTarget
                : TeamChallengeUtility.ResolveKillTarget(frame, definition);

            descriptionText.text = definition != null ? definition.ResolveDescription(killTarget) : string.Empty;
        }

        if (challenge.State == TeamChallengeState.Starting)
        {
            if (titleText != null)
                titleText.text = "GET READY!";

            // Nothing to track yet - without this the progress readout shows whatever it last held
            // (the authored placeholder text, or a previous challenge's final "20 / 20") until the
            // first ChallengeActive tick overwrites it.
            if (objectiveText != null)
                objectiveText.text = string.Empty;

            // Hidden rather than shown as "0" - RemainingCountdown crossing to <= 0 flips the
            // challenge straight to ChallengeActive the same tick (TeamChallengeSystem.TickStarting),
            // so this is mostly a defensive guard against a prediction/rollback frame that reads
            // State-Starting with a hit-zero countdown. ChallengeCountdownWidget.Show handles it.
            if (countdown != null)
                countdown.Show(challenge.RemainingCountdown.AsFloat);

            return;
        }

        // Starting is the only branch that ever shows the countdown - once the challenge moves on
        // (ChallengeActive/RewardAvailable/Completed/Failed) nothing above touches it again, so
        // without this it would stay stuck on-screen showing its last digit (e.g. "1") for the
        // rest of the attempt. Hide also resets the widget's tick-sound latch for the next challenge.
        if (countdown != null)
            countdown.Hide();

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
