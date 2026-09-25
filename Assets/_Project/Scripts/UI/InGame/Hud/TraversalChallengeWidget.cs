using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible, whole-team HUD banner for an Active Traversal Challenge (see
// docs/traversal-challenge.md) - lives under the normal HUD, same idiom
// BreathingWidget already uses for "NEXT ASSAULT 00:30". Deliberately NOT a per-entity
// world-following widget (an earlier version tried that and was corrected - a floating marker
// anchored to the activator's own world Transform would only be visible to whichever player is
// actually looking at that spot, but the whole point is the pause/no-new-spawns effect is
// GLOBAL for the team - one player activates it, everyone should see the countdown regardless of
// where they are in the level). One shared instance for the whole HUD, not per local-player-slot,
// same reasoning BreathingWidget's own Skip Vote UI already documents.
//
// Shown only while Global.CurrentState == GameState.TraversalChallenge - a real GameState value
// (see GameState.qtn's own comments) is what keeps this mutually exclusive with
// SurvivalWidget/BossWidget/TeamChallengeWidget, rather than each of them independently
// re-deriving "am I the one that should show". Extends GameStateGatedWidget, reacting to the real
// GameStateChanged event instead of polling.
public class TraversalChallengeWidget : GameStateGatedWidget
{
    [SerializeField] private TMP_Text countdownText;

    // True from OnActivated/OnCompleted/OnFailed until the matching AnnouncerManager banner has
    // FULLY finished playing - ORed into IsActive so this widget's countdown panel stays shown for
    // the tail of that announcement even after Global.CurrentState has already left
    // TraversalChallenge (it flips away almost immediately once the challenge ends), same pattern
    // TeamChallengeWidget's own _bannerPlaying uses. Cleared by AnnouncerManager's own onComplete
    // callback, which also asks the base class to recompute visibility now that this flag changed.

    protected override void Awake()
    {
        base.Awake();
        QuantumEvent.Subscribe<EventTraversalChallengeActivated>(this, OnActivated);
        QuantumEvent.Subscribe<EventTraversalChallengeCompleted>(this, OnCompleted);
        QuantumEvent.Subscribe<EventTraversalChallengeFailed>(this, OnFailed);
    }

    // No OnDestroy override needed - the base class's own QuantumEvent.UnsubscribeListener(this)
    // removes every subscription tied to this instance regardless of which class subscribed it,
    // covering this widget's own announcement-event subscriptions above too.

    // Fire unconditionally on every connected client - same whole-team-awareness reasoning this
    // widget's own countdown banner above already documents, not personal feedback. AnnouncerManager
    // is one-at-a-time, not a queue (see docs/announcer.md) - if more than one Traversal Challenge is
    // ever Active at once, a second announcement restarts the banner with its own new text rather
    // than both playing out in full; accepted, same "at most a handful of these POIs are expected to
    // ever exist in a level" simplification the rest of this feature already leans on.
    private void OnActivated(EventTraversalChallengeActivated e)
    {
        AnnouncerManager.Instance?.Announce("TRAVERSAL CHALLENGE STARTED", OnBannerComplete);
    }

    private void OnCompleted(EventTraversalChallengeCompleted e)
    {
        AnnouncerManager.Instance?.Announce("TRAVERSAL CHALLENGE COMPLETE", OnBannerComplete);
    }

    private void OnFailed(EventTraversalChallengeFailed e)
    {
        AnnouncerManager.Instance?.Announce("TRAVERSAL CHALLENGE FAILED", OnBannerComplete);
    }

    private void OnBannerComplete()
    {
        RequestRecompute();
    }

    protected override bool IsActive(GameState currentGameState)
    {
        return currentGameState == GameState.TraversalChallenge;
    }

    protected override unsafe void OnActiveQUpdate(Frame frame)
    {
        // Only keeping the panel up for the tail of the announcement - no live countdown to read
        // anymore once CurrentState has actually left TraversalChallenge.
        if (frame.Global->CurrentState != GameState.TraversalChallenge)
            return;

        if (countdownText != null)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(frame.Global->TraversalChallengeTimeRemaining.AsFloat, 0f));
            countdownText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
