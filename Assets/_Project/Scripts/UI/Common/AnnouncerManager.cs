using System;
using System.Collections.Generic;
using Quantum;
using TMPro;
using UnityEngine;

// One shared "big banner" text announcement (fade + slide in, hold, fade + slide out) for rare,
// dramatic, whole-team moments - "AREA SECURED", "SURVIVAL MODE STARTED", "CHALLENGE STARTED/
// COMPLETE/FAILED", etc. - so every caller shares one authored visual/tuning instead of each HUD
// widget owning its own copy of the same fade+slide tween code (which is how this file replaced
// three near-identical copies: BreathingWidget's own AREA SECURED sequence, the
// standalone SurvivalStartedWidget, and AnnouncementBannerWidget). The actual fade/slide/hold is
// AppearSlideHorizontally's job; this just swaps the label text and forwards Show/Hidden as a
// simple Announce(message, onComplete) call any widget can make.
//
// Owns BOTH triggers driven purely by GameState transitions ("AREA SECURED", "SURVIVAL MODE
// STARTED" - see OnGameStateChanged below) AND the ones fired by other widgets for their own
// non-GameState events (TeamChallengeWidget/TraversalChallengeWidget's own CHALLENGE STARTED/
// COMPLETE/FAILED, tied to Quantum events those widgets already own). Centralizing the GameState-
// driven ones HERE - listening to the real GameStateChanged event directly - is deliberate: this
// is what lets SurvivalWidget/BreathingWidget stay pure "listen to GameState, show/hide myself"
// widgets with zero announcer awareness, no delay/wait-for-banner coordination, and no risk of
// misfiring off a Team/Traversal Challenge overlay - GameStateChanged's own PreviousState already
// tells the difference for free (a challenge ending into an already-secured Breathing has
// PreviousState == TeamChallenge/TraversalChallenge, not Survival, so OnGameStateChanged's own
// rules below naturally skip it - no widget-side tracking needed at all).
//
// A real FIFO queue, not "restart in place": a second Announce() call while one is already playing
// is queued and plays after the current one fully finishes, in order, rather than clobbering it.
// This matters beyond ordering - every caller's onComplete is GUARANTEED to eventually fire (once
// its own turn plays out), so a caller latching a bool on Announce() and clearing it in onComplete
// can never get stuck permanently true from a competing Announce() overwriting its pending
// callback - that WAS a real bug under the old restart-in-place behavior.
//
// Registry of live managers rather than a single static field - same reasoning as ToastManager:
// the gameplay HUD scene can reload independently of whatever else is loaded, and a naive static
// field would end up pointing at a manager destroyed with its old scene.
public class AnnouncerManager : MonoBehaviour
{
    private static readonly List<AnnouncerManager> Managers = new List<AnnouncerManager>();

    // Always a REAL null when there is no usable manager - never a destroyed one - so every
    // `AnnouncerManager.Instance?.Announce(...)` call site stays correct exactly as written.
    public static AnnouncerManager Instance
    {
        get
        {
            for (int i = Managers.Count - 1; i >= 0; i--)
            {
                if (Managers[i] != null)
                    return Managers[i];

                Managers.RemoveAt(i);
            }

            return null;
        }
    }

    // Statics survive a Play Mode exit when Enter Play Mode Options disables domain reload - same
    // reset ToastManager/AudioManager do, and for the same reason: a stale entry here would be a
    // destroyed manager from the previous session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Managers.Clear();
    }

    [SerializeField] private AppearSlideHorizontally slide;
    [SerializeField] private TMP_Text text;
    [SerializeField, Tooltip("How long the banner stays fully visible before it auto-hides. Owned HERE, not AppearSlideHorizontally's own autoHideAfter - that field defaults to 0 ('never auto-hide, call Hide() yourself'), so leaving it unauthored on the scene instance would make every announcement show once and then sit on screen forever. Managing the timer here means Announce() works correctly regardless of that field's Inspector value.")]
    private float displayDuration = 2.5f;

    // True while a banner is currently showing OR one is queued behind it - a caller that needs to
    // keep its own UI up for the tail of an announcement (e.g. TeamChallengeWidget's own root) can
    // read this instead of latching its own bool.
    public bool IsPlaying => _isPlaying || _queue.Count > 0;

    private readonly Queue<PendingAnnouncement> _queue = new Queue<PendingAnnouncement>();
    private readonly struct PendingAnnouncement
    {
        public readonly string Message;
        public readonly Action OnComplete;

        public PendingAnnouncement(string message, Action onComplete)
        {
            Message = message;
            OnComplete = onComplete;
        }
    }

    private bool _isPlaying;
    private Action _pendingComplete;
    private float _hideTimer;

    private void Awake()
    {
        Managers.Remove(this);
        Managers.Add(this);

        if (slide != null)
            slide.Hidden += OnHidden;

        QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
    }

    // Defensive: the banner must start hidden regardless of how the scene authored the slide
    // GameObject's own initial active state - Announce() is the only thing that should ever show
    // it. Deferred to Start (not Awake) so AppearSlide's own Awake has already captured its rest
    // position/alpha baseline off whatever the Inspector authored before this deactivates it.
    private void Start()
    {
        if (slide != null && slide.gameObject.activeSelf == true)
            slide.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Managers.Remove(this);

        if (slide != null)
            slide.Hidden -= OnHidden;

        QuantumEvent.UnsubscribeListener(this);
    }

    // The only two GameState transitions worth a whole-team announcement. Checked against
    // PreviousState/NewState directly off the real event - deliberately NOT reconstructed from a
    // locally-tracked "last state I saw", which is exactly what caused this to misfire off a Team/
    // Traversal Challenge overlay before this widget-side logic was centralized here (see
    // docs/announcer.md). Boss/Upgrade/RunFailed/Victory/Lobby exits (and anything from Upgrade)
    // are deliberately not called out here at all - not a fresh "assault begins" moment.
    private void OnGameStateChanged(EventGameStateChanged e)
    {
        // GameState.Breathing is only ever entered once the area is actually secured (see
        // CombatDirectorSystem.ResolveDesiredState) - PreviousState == Survival is what makes this
        // a genuine secure edge rather than a Team/Traversal Challenge overlay clearing to reveal
        // an already-secured Breathing (that transition's PreviousState is the challenge itself).
        if (e.NewState == GameState.Breathing && e.PreviousState == GameState.Survival)
        {
            Announce("AREA SECURED");
            return;
        }

        // Match start (Lobby -> Survival) or a Breathing Break ending (Breathing -> Survival) -
        // NOT a Team/Traversal Challenge overlay clearing to reveal the Survival that was already
        // there underneath (that transition's PreviousState is the challenge itself, not Lobby/
        // Breathing).
        if (e.NewState == GameState.Survival
            && (e.PreviousState == GameState.Lobby || e.PreviousState == GameState.Breathing))
        {
            Announce("SURVIVAL MODE STARTED");
        }
    }

    // Unscaled, matching every other HUD banner timer in this codebase - a Level-Up screen can ramp
    // Time.timeScale down match-wide, and that shouldn't stretch how long an announcement lingers.
    private void Update()
    {
        if (_hideTimer <= 0f)
            return;

        _hideTimer -= Time.unscaledDeltaTime;

        if (_hideTimer <= 0f)
            slide.Hide();
    }

    // Guarantees onComplete eventually fires exactly once, either right away (nothing to play with)
    // or once this announcement's own turn has fully played out - never silently drops it, so a
    // caller latching a bool here and clearing it in onComplete can never get stuck.
    public void Announce(string message, Action onComplete = null)
    {
        if (slide == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (_isPlaying == true)
        {
            _queue.Enqueue(new PendingAnnouncement(message, onComplete));
            return;
        }

        PlayNow(message, onComplete);
    }

    private void PlayNow(string message, Action onComplete)
    {
        _isPlaying = true;

        if (text != null)
            text.text = message;

        _pendingComplete = onComplete;
        _hideTimer = displayDuration;
        slide.Show();
    }

    private void OnHidden()
    {
        _hideTimer = 0f;
        _isPlaying = false;

        Action callback = _pendingComplete;
        _pendingComplete = null;
        callback?.Invoke();

        if (_queue.Count > 0)
        {
            PendingAnnouncement next = _queue.Dequeue();
            PlayNow(next.Message, next.OnComplete);
        }
    }
}
