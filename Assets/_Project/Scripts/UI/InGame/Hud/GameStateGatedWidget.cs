using Quantum;
using QuantumUser.View;
using UnityEngine;

// Shared base for a top-screen HUD banner gated on Global.CurrentState, reacting to the real
// GameStateChanged Quantum event instead of each widget independently polling+diffing its own
// SetActive boolean every tick - possible now that TeamChallenge/TraversalChallenge are real
// GameState values (see GameState.qtn's own comments) instead of a separate, event-less
// HudBannerKind field.
//
// Owns: event subscribe/unsubscribe, the initial-sync-at-QStart correctness fix (a late-joining/
// reconnecting client must see the CORRECT banner immediately - e.g. reconnecting mid-Boss-fight -
// not wait for the NEXT transition, which may never come again this match), and the shared
// SetShown(GameObject,bool) diff-toggle. Derived widgets only implement IsActive (their own
// GameState match, optionally combined with an extra per-widget condition) and, optionally,
// OnShownChanged (transition side effects: animations, one-shot resets) and OnActiveQUpdate
// (per-tick content refresh while shown - a plain poll, same idiom every widget already used,
// since Quantum's rollback/prediction model makes local-frame reads normal for numeric readouts).
public abstract class GameStateGatedWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField, Tooltip("Container for the widget's visible children - toggled by this base class. Must be a CHILD GameObject, not the GameObject this script itself lives on, since QUpdate stops firing once its own GameObject is disabled.")]
    protected GameObject root;

    private GameState _currentGameState;
    private bool _shown;
    private bool _initialized;

    protected virtual void Awake()
    {
        QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChangedEvent);
    }

    protected virtual void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    public override unsafe void QStart(QuantumGame game)
    {
        // Seed correct visibility immediately, unconditionally - see this class's own header
        // comment on why a late joiner can't wait for the next GameStateChanged edge.
        _currentGameState = game.Frames.Predicted.Global->CurrentState;
        Apply(force: true);
    }

    public override void QUpdate(QuantumGame game)
    {
        if (_shown == true)
            OnActiveQUpdate(game.Frames.Predicted);
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    // Call whenever a per-widget extra condition changes OUTSIDE a GameStateChanged event (e.g. an
    // AnnouncerManager onComplete callback, or an edge-detected sim flag) - re-evaluates IsActive()
    // against the last-known CurrentGameState and re-applies.
    protected void RequestRecompute()
    {
        Apply(force: false);
    }

    private void OnGameStateChangedEvent(EventGameStateChanged e)
    {
        _currentGameState = e.NewState;
        Apply(force: false);
    }

    private void Apply(bool force)
    {
        bool shown = IsActive(_currentGameState);

        if (force == false && _initialized == true && shown == _shown)
            return;

        _initialized = true;
        _shown = shown;
        SetShown(root, shown);
        OnShownChanged(shown, _currentGameState);
    }

    // Must be pure/cheap - called on every GameStateChanged event, every RequestRecompute() call,
    // and once at QStart. Simple equality for a pure-state widget; combine with an extra
    // per-widget field for one with AND/OR conditions.
    protected abstract bool IsActive(GameState currentGameState);

    // Fires once per actual shown/hidden transition (never redundantly) - override for one-shot
    // side effects (start/stop an animation, latch/reset a flag). Not called every tick.
    // currentGameState is whatever state caused this transition - passed through directly (rather
    // than a subclass maintaining its own shadow copy from its own QUpdate) since this can also
    // fire from the GameStateChanged event path, whose ordering relative to any subclass's own
    // QUpdate override isn't guaranteed.
    protected virtual void OnShownChanged(bool shown, GameState currentGameState)
    {
    }

    // Called every tick ONLY while shown == true - override for per-tick content refresh
    // (slider/text values, live filters).
    protected virtual void OnActiveQUpdate(Frame frame)
    {
    }

    protected static void SetShown(GameObject go, bool shown)
    {
        if (go != null && go.activeSelf != shown)
            go.SetActive(shown);
    }
}
