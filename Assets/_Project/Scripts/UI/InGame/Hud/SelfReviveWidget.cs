using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Dedicated small HUD element shown to an incapacitated (Downed/KO) LOCAL player with a SELF REVIVE
// button - see docs/revive.md. A Widget, not a Window - it's a self-polling QuantumGlobalMonoBehaviour
// (same shape as SkillCooldownUiWidget/CurrencyUiWidget's own "self-binds to a local slot, polls
// Quantum state every QUpdate" pattern), NOT a UiWindow subclass like BossWindow/ChooseWindow -
// those are thin presentation shells driven externally by a separate poller (BossWidget, in
// BossWindow's case); this instead owns both the polling and the presentation itself, one class,
// same reasoning every other per-local-slot HUD widget in this codebase already follows. Also
// deliberately separate from ChooseWindow (this codebase already has a strong precedent against
// building a second, drifting-prone parallel copy of THAT specific window - see
// docs/choice-window-refactor.md) and from the world-space InteractionPromptWidget/
// SkillCooldownUiWidget's own teammate-revive hold-progress display - self-revive is a single
// press/confirm (SelfReviveCommand), not a hold - and only works while Downed, same as a
// teammate's own hold (see PlayerLifeStateUtility.EnterKO) - KO is a dead end with no revive path
// at all anymore, so this widget hides both the charges readout and the button itself once KO'd
// (see QUpdate). While still Downed in co-op, this widget lets a player choose to press it (spend
// a charge) or simply do nothing and wait for a teammate's own hold-to-revive to complete instead -
// both are always simultaneously valid, this widget never blocks or races against a teammate's
// channel.
//
// Self-binds to local slot 0 by default (same MyLocalPlayer.Instance.BindToSlot pattern
// SkillCooldownUiWidget/CurrencyUiWidget already use), so a second scene instance with
// localSlotIndex = 1 covers couch co-op's second local (split-screen) player independently.
public class SelfReviveWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField, Tooltip("Optional - bare SelfReviveCharges number, e.g. \"2\" (no surrounding label text - pair with an authored icon/label in the prefab if wanted). Left unassigned, this feature is simply off.")]
    private TMP_Text chargesText;
    [SerializeField] private UnityEngine.UI.Button selfReviveButton;
    [SerializeField, Tooltip("Optional - counts PlayerLifeState.BleedOutRemaining down while Downed (not KO - there's no timer once KO'd). Left unassigned, this feature is simply off.")]
    private TMP_Text bleedOutTimerText;
    [SerializeField, Tooltip("Hex color (e.g. \"#FD3971\") wrapped via TMP rich text around just the state word - \"YOU ARE <color=#FD3971>DOWNED</color>\" - not the whole titleText. Requires Rich Text enabled on titleText (TMP's own default).")]
    private string titleHighlightColorHex = "#FD3971";

    [Header("Being Revived (teammate hold - see docs/revive.md)")]
    [SerializeField, Tooltip("Optional - Slider.value (0-1) driven by PlayerLifeState.ReviveProgress/ReviveConfig.DownedReviveDuration while a TEAMMATE is holding to revive this player. Hidden entirely whenever nobody is holding (not just reset to 0), so an interrupted hold's stale progress never lingers on screen. Left unassigned, this feature is simply off.")]
    private Slider reviveProgressSlider;
    [SerializeField, Tooltip("Optional container shown alongside reviveProgressSlider while a teammate is holding - e.g. a \"BEING REVIVED\" label. Left unassigned, only the slider itself is toggled.")]
    private GameObject beingRevivedRoot;

    [SerializeField, Tooltip("On: binds itself to localSlotIndex automatically. Off: stays unbound until something else calls Initialize (e.g. a future party HUD).")]
    private bool autoBindLocalSlot = true;
    [SerializeField, Tooltip("Local slot index to bind to when autoBindLocalSlot is on - 0 for player 1, 1 for a second local (couch co-op) player.")]
    private int localSlotIndex;

    private EntityRef _entityRef;
    private bool _shown;

    private void Start()
    {
        if (autoBindLocalSlot)
            MyLocalPlayer.Instance.BindToSlot(localSlotIndex, Initialize);

        if (selfReviveButton != null)
            selfReviveButton.onClick.AddListener(OnSelfReviveClicked);

        // Force-hides regardless of whatever visualRoot's own active state was left at in the
        // Editor/prefab - NOT SetShown(false), which no-ops here since _shown already defaults to
        // false in C# (its own "skip redundant SetActive" guard reads as "already hidden" even
        // though the GameObject itself might still be active).
        _shown = false;
        if (visualRoot != null)
            visualRoot.SetActive(false);

        SetBeingRevivedShown(false);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called externally (e.g. a future party HUD) so an externally-driven instance never fights
    // its own default self-binding - same convention SkillCooldownUiWidget/CurrencyUiWidget use.
    public void DisableAutoBind()
    {
        autoBindLocalSlot = false;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        if (_entityRef == EntityRef.None)
            return;

        Frame frame = game.Frames.Predicted;
        bool incapacitated = PlayerLifeStateUtility.IsIncapacitated(frame, _entityRef);

        SetShown(incapacitated);

        if (incapacitated == false)
        {
            // visualRoot going inactive already hides these, but only if they live under it -
            // they're optional, hand-placed references that don't have to. Cleared explicitly so a
            // completed/interrupted revive never leaves a stale bar behind either way.
            SetBeingRevivedShown(false);
            return;
        }

        bool hasLifeState = frame.Unsafe.TryGetPointer<PlayerLifeState>(_entityRef, out var lifeState);
        bool isKo = hasLifeState == true && lifeState->State == PlayerLifeStateKind.KO;

        // A teammate actively holding to pick this player up (see RefreshBeingRevived) - resolved
        // here rather than inside that method because the title reads off it too.
        bool beingRevived = hasLifeState == true && isKo == false && lifeState->ReviveHolder != EntityRef.None;

        if (titleText != null)
        {
            // "YOU ARE DOWNED" is a status; while somebody is actually picking you up the panel
            // has something more useful to say, so the title takes over for the duration of the
            // hold and reverts the instant it's interrupted (same self-healing poll as everything
            // else here - no state is tracked for it).
            titleText.text = isKo
                ? $"YOU ARE <color={titleHighlightColorHex}>KO'D</color>"
                : beingRevived == true
                    ? $"<color={titleHighlightColorHex}>BEING REVIVED</color>"
                    : $"YOU ARE <color={titleHighlightColorHex}>DOWNED</color>";
        }

        // Only Downed has a bleed-out clock - KO has no timer, you just wait for a revive (see
        // docs/revive.md). Reads the live value directly, so it automatically reflects the
        // simulation's own pause-while-held behavior (PlayerLifeStateSystem) with no extra UI logic.
        if (bleedOutTimerText != null)
        {
            bleedOutTimerText.gameObject.SetActive(isKo == false);

            if (isKo == false && hasLifeState == true)
                bleedOutTimerText.text = FormatBleedOutTimer(lifeState->BleedOutRemaining);
        }

        // A teammate's hold-to-revive progress, on the downed player's OWN HUD. The world-space
        // InteractionPromptWidget above their head shows the same bar, but that one is anchored to
        // a character the downed player isn't necessarily looking at (and is easy to miss in a
        // crowded fight) - this panel is where their eyes already are, so the feedback belongs here
        // too. Reads the target-side PlayerLifeState directly rather than the reviver's own
        // ReviveChannel, so it doesn't care WHO is holding (or that they're a remote player).
        RefreshBeingRevived(frame, beingRevived, lifeState);

        byte charges = frame.Unsafe.TryGetPointer<CharacterStats>(_entityRef, out var stats) == true ? stats->SelfReviveCharges : (byte)0;

        // Self-revive only works while Downed anymore - KO is a dead end (no teammate hold either,
        // see PlayerLifeStateUtility.EnterKO/ReviveUtility) with no path back except
        // Global.BreathingAreaSecured auto-reviving everyone still incapacitated, so both the
        // charges readout and the button itself are hidden entirely once KO'd rather than just
        // disabled - there's nothing left to spend charges on, unlike HealingShrineUtility's own
        // "let the press fail loudly rather than hide the button" precedent (that's for a press
        // that's merely pointless right now, not permanently unusable for the rest of this life
        // state).
        if (chargesText != null)
        {
            chargesText.gameObject.SetActive(isKo == false);

            if (isKo == false)
                chargesText.text = charges.ToString();
        }

        if (selfReviveButton != null)
        {
            selfReviveButton.gameObject.SetActive(isKo == false);
            selfReviveButton.interactable = isKo == false && charges > 0;
        }
    }

    private void OnSelfReviveClicked()
    {
        _game?.SendCommand(localSlotIndex, new SelfReviveCommand());
    }

    // Shown only while somebody is actually holding (PlayerLifeState.ReviveHolder != None, the same
    // field that pauses the bleed-out clock), and never while KO'd - KO has no revive path at all
    // anymore (see PlayerLifeStateUtility.EnterKO), so there can be no hold in progress to show.
    // Both conditions are folded into the caller's own `beingRevived`, which the title reads too.
    private unsafe void RefreshBeingRevived(Frame frame, bool beingRevived, PlayerLifeState* lifeState)
    {
        if (beingRevived == false || lifeState == null)
        {
            SetBeingRevivedShown(false);
            return;
        }

        SetBeingRevivedShown(true);

        if (reviveProgressSlider == null)
            return;

        ReviveConfig config = PlayerLifeStateUtility.GetConfig(frame);
        FP duration = config != null ? config.DownedReviveDuration : (FP._2 + FP._0_50);

        reviveProgressSlider.value = duration > FP._0 ? (lifeState->ReviveProgress / duration).AsFloat : 0f;
    }

    private void SetBeingRevivedShown(bool shown)
    {
        SetActive(beingRevivedRoot, shown);

        if (reviveProgressSlider != null)
            SetActive(reviveProgressSlider.gameObject, shown);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private static string FormatBleedOutTimer(FP secondsRemaining)
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining.AsFloat));
        return $"{seconds}s";
    }

    private void SetShown(bool shown)
    {
        if (_shown == shown || visualRoot == null)
            return;

        _shown = shown;
        visualRoot.SetActive(shown);
    }
}
