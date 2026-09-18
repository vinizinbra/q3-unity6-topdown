using Quantum;
using QuantumUser.View;
using UnityEngine;

// Solo-only tutorial popups (HowToPlayPopup/FirstBreakPopup) - pure local/View decision: the sim
// dispatches nothing tutorial-specific, this manager reacts to already-existing sim state/events,
// checks solo (human players only - bots don't count, see IsSolo) and opens the matching popup by
// TYPE via InMatchPopupManager.Open<T>, sending SetTutorialPauseCommand(Paused = true) itself right
// after - each popup then unpauses itself on close (see TutorialPopup.Close). Whether a popup
// should be skipped because it's already been seen (PlayerPrefs) is a later addition, not
// implemented yet.
public class InMatchTutorialManager : QuantumGlobalMonoBehaviour
{
    // Set once each popup's own trigger condition fires, cleared once it's actually opened - kept
    // pending (instead of opening immediately) while GameState.Upgrade is active, so a level-up/
    // Chest Choice Window in progress is never fought over/covered by a tutorial popup. Re-checked
    // every tick, so it opens the moment that window closes rather than being skipped outright.
    private bool _howToPlayPending;
    private bool _firstBreakPending;

    // Edge-detects Global.BreathingAreaSecured flipping true during the run's FIRST Breathing Break
    // only (BreathingIndex == 0) - see QUpdate. FirstBreakPopup is meant to show once the enemies
    // are actually cleared (the "AREA SECURED" moment BreathingWidget's own banner reacts
    // to), not the instant the phase merely becomes Breathing, since enemies are deliberately left
    // alive when a Breathing Break begins (see docs/run-phase.md).
    private bool _firstBreakAreaWasSecured;

    // Set the first time the area-secured edge ever arms _firstBreakDelayTimer, never cleared - a
    // Team/Traversal Challenge overlay starting or ending during the run's first Breathing Break
    // can flip Global.CurrentState away from and back to Breathing (see GameState.qtn's own
    // TeamChallenge/TraversalChallenge comments), which would otherwise re-trigger
    // _firstBreakAreaWasSecured's rising edge and re-arm the popup a second time.
    private bool _firstBreakPopupArmedOrShown;

    // Counts down once the area-secured edge fires, so the popup doesn't pop up on top of/racing
    // BreathingWidget's own "AREA SECURED" banner reveal - 0 means no delay pending.
    // Unscaled, same reasoning every other popup/HUD timer in this codebase uses.
    private const float FirstBreakDelaySeconds = 2f;
    private float _firstBreakDelayTimer;

    private void Awake()
    {
        QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        bool isFirstBreathing = frame.Global->CurrentState == GameState.Breathing && frame.Global->BreathingIndex == 0;
        bool secured = isFirstBreathing && frame.Global->BreathingAreaSecured;

        if (secured && _firstBreakAreaWasSecured == false && IsSolo(frame) && _firstBreakPopupArmedOrShown == false)
        {
            _firstBreakDelayTimer = FirstBreakDelaySeconds;
            _firstBreakPopupArmedOrShown = true;
        }

        _firstBreakAreaWasSecured = secured;

        if (_firstBreakDelayTimer > 0f)
        {
            _firstBreakDelayTimer -= Time.unscaledDeltaTime;

            if (_firstBreakDelayTimer <= 0f)
                _firstBreakPending = true;
        }

        // Also held off while orbs are still being vacuumed in right after the area secures
        // (Global.OrbVacuumTimeRemaining) - that sweep can itself cross an XP threshold and open
        // the level-up screen a moment after BreathingAreaSecured flips, before CurrentState has
        // even reached Upgrade yet this tick. AND while a multi-level XP grant still has more
        // chained screens queued (Global.DebugPendingLevelUps) - DebugCheatSystem.
        // TryOpenNextPendingLevelUp drains one level per screen, leaving a real one-tick gap
        // between screens where LevelUpScreenOpen is briefly false and CurrentState briefly
        // reverts to Breathing; without this check FirstBreakPopup could open in that gap and end
        // up stacked on top of the next chained level-up screen.
        bool upgradePending = frame.Global->CurrentState == GameState.Upgrade
            || frame.Global->OrbVacuumTimeRemaining.AsFloat > 0f
            || frame.Global->DebugPendingLevelUps > 0;

        if (upgradePending)
            return;

        if (_howToPlayPending)
        {
            _howToPlayPending = false;
            Open<HowToPlayPopup>();
        }

        if (_firstBreakPending)
        {
            _firstBreakPending = false;
            Open<FirstBreakPopup>();
        }
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    private unsafe void OnGameStateChanged(EventGameStateChanged e)
    {
        if (e.PreviousState != GameState.Lobby || e.NewState != GameState.Survival)
            return;

        if (IsSolo(_game.Frames.Predicted) == false)
            return;

        _howToPlayPending = true;
    }

    private static void Open<T>() where T : TutorialPopup
    {
        InMatchPopupManager.instance.Open<T>();
        TutorialPopup.SendPause(true);
    }

    // Human players only - bots don't count against solo (see docs/bots.md: a person testing
    // co-op paths alone with BotBrain teammates should NOT see these).
    private static unsafe bool IsSolo(Frame frame)
    {
        int humanCount = 0;
        var players = frame.Filter<PlayerLink>();

        while (players.Next(out EntityRef entity, out PlayerLink _))
        {
            if (frame.Has<BotBrain>(entity) == false)
                humanCount++;
        }

        return humanCount <= 1;
    }
}
