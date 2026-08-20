using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible, whole-team HUD banner for an Active Traversal Challenge (see
// docs/traversal-challenge.md) - lives under the normal HUD (GameplayWindow), same idiom
// BreathingCountdownWidget already uses for "NEXT ASSAULT 00:30". Deliberately NOT a per-entity
// world-following widget (an earlier version tried that and was corrected - a floating marker
// anchored to the activator's own world Transform would only be visible to whichever player is
// actually looking at that spot, but the whole point is the pause/no-new-spawns effect is
// GLOBAL for the team - one player activates it, everyone should see the countdown regardless of
// where they are in the level). One shared instance for the whole HUD, not per local-player-slot,
// same reasoning BreathingCountdownWidget's own Skip Vote UI already documents.
//
// Shown only while Global.HudBanner == HudBannerKind.TraversalChallenge - that single shared value
// (resolved once a tick by CombatDirectorSystem.ApplyHudBanner) is what keeps this mutually
// exclusive with DirectorTimelineUiWidget/BossWidget, rather than each of the three independently
// re-deriving "am I the one that should show" (see GameState.qtn's own HudBannerKind comment).
public class TraversalChallengeWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text countdownText;

    private void Awake()
    {
        QuantumEvent.Subscribe<EventTraversalChallengeActivated>(this, OnActivated);
        QuantumEvent.Subscribe<EventTraversalChallengeCompleted>(this, OnCompleted);
        QuantumEvent.Subscribe<EventTraversalChallengeFailed>(this, OnFailed);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    // Unlike InteractionPromptWidget's own toast (filtered to presses by this client's own local
    // players), these fire unconditionally on every connected client - same whole-team-awareness
    // reasoning this widget's own countdown banner above already documents, not personal feedback.
    // If more than one Traversal Challenge is ever Active at once, each instance's own event still
    // fires its own toast independently (unlike the countdown, which only reflects whichever one
    // ticked last) - an overlapping toast burst in that edge case is accepted, not a bug.
    private void OnActivated(EventTraversalChallengeActivated e)
    {
        ToastManager.Instance?.Show("TRAVERSAL CHALLENGE STARTED");
    }

    private void OnCompleted(EventTraversalChallengeCompleted e)
    {
        ToastManager.Instance?.Show("TRAVERSAL CHALLENGE COMPLETE");
    }

    private void OnFailed(EventTraversalChallengeFailed e)
    {
        ToastManager.Instance?.Show("TRAVERSAL CHALLENGE FAILED");
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;
        bool active = frame.Global->HudBanner == HudBannerKind.TraversalChallenge;

        SetShown(root, active);

        if (active == false)
            return;

        if (countdownText != null)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(frame.Global->TraversalChallengeTimeRemaining.AsFloat, 0f));
            countdownText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }

    private static void SetShown(GameObject go, bool shown)
    {
        if (go == null)
            return;

        if (go.activeSelf != shown)
            go.SetActive(shown);
    }
}
