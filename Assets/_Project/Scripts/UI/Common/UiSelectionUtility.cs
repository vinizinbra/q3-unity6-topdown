using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shared gamepad/joystick default-selection helper for UiWindow/UiPopup - both hierarchies want
// "focus the first interactable Selectable the instant this is shown," deferred one frame (so a
// subclass's own Show() override, which may still be tweaking button active-state after its own
// base.Show() call - e.g. InMatchSettingsPopup's restartButton - has finished first) and, if that
// target plays a ShakeGrowImpactAnimation pop-in (e.g. ChooseWindow's cards), further deferred
// until that animation has actually landed - a controller player should never see focus show up on
// a button that still looks mid-animation.
public static class UiSelectionUtility
{
    public static IEnumerator SelectFirstInteractableNextFrame(MonoBehaviour owner)
    {
        yield return null;

        if (owner == null)
            yield break;

        Selectable[] selectables = owner.GetComponentsInChildren<Selectable>(includeInactive: false);

        foreach (Selectable selectable in selectables)
        {
            if (selectable.interactable)
            {
                SelectFirstInteractable(selectable);
                yield break;
            }
        }
    }

    // Call every tick from a screen whose Selectables' own interactable state can change out from
    // under the current EventSystem selection while it stays open (e.g. Store - a purchase flips
    // that offer's own button to interactable=false via PurchasableCardUi, but the player can keep
    // buying other things). If the currently selected Selectable just went non-interactable (or
    // inactive), hands focus to whichever OTHER active+interactable Selectable under `scope` sits
    // closest to it on screen - Unity's own Automatic navigation only resolves neighbors when the
    // player presses a direction, it never re-homes an already-selected object that goes stale out
    // from under it, so without this the hand/highlight is left parked on a dead button until the
    // player nudges the stick themselves.
    // fallback (e.g. ChooseWindow's own secondaryButton - Close/Cancel/Keep Current) is the
    // guaranteed escape hatch for when every card in scope has gone non-interactable at once (e.g.
    // every Store offer sold out/unaffordable) - used only when the nearest-in-scope search below
    // comes up empty, so a still-interactable card always wins over it on proximity.
    // Prefers EventSystem.current.currentSelectedGameObject's own Selectable (the authoritative
    // live state for real gamepad/keyboard navigation) and only falls back to lastClicked (e.g.
    // ChooseWindow tracks whichever Selectable it last clicked) when EventSystem has nothing -
    // confirmed via diagnostics that a mouse click doesn't reliably leave currentSelectedGameObject
    // pointing at what was clicked in this project's input setup, so relying on it alone would
    // silently no-op for a mouse/touch player.
    public static void ReselectIfNoLongerInteractable(Selectable lastClicked, Transform scope, Selectable fallback = null)
    {
        if (scope == null)
            return;

        GameObject selectedGO = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        Selectable current = selectedGO != null ? selectedGO.GetComponent<Selectable>() : null;

        if (current == null)
            current = lastClicked;

        if (current == null)
            return;

        if (current.interactable && current.gameObject.activeInHierarchy)
            return;

        Selectable[] candidates = scope.GetComponentsInChildren<Selectable>(includeInactive: false);
        Vector2 fromPosition = current.transform.position;

        Selectable closest = null;
        float closestDistanceSq = float.MaxValue;

        foreach (Selectable candidate in candidates)
        {
            if (candidate == current || candidate.interactable == false)
                continue;

            float distanceSq = ((Vector2)candidate.transform.position - fromPosition).sqrMagnitude;

            if (distanceSq < closestDistanceSq)
            {
                closestDistanceSq = distanceSq;
                closest = candidate;
            }
        }

        if (closest == null && fallback != null && fallback.interactable && fallback.gameObject.activeInHierarchy)
            closest = fallback;

        if (closest != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(closest.gameObject);
    }

    // True while any intro animation under `root` is still running: a ShakeGrowImpactAnimation, or a
    // one-shot UiTween (ScaleTween...). Looping/ping-pong tweens never "finish", so they don't count.
    // Used to keep a freshly shown popup/card from being focused or pressed while it still looks mid-pop.
    public static bool IsAnimating(Transform root)
    {
        if (root == null)
            return false;

        foreach (ShakeGrowImpactAnimation shake in root.GetComponentsInChildren<ShakeGrowImpactAnimation>(false))
        {
            if (shake.IsPlaying)
                return true;
        }

        foreach (UiTween tween in root.GetComponentsInChildren<UiTween>(false))
        {
            if (tween.IsPlaying && tween.playType == UiTween.UiPlayType.ONCE)
                return true;
        }

        return false;
    }

    // Same check for one object: its own and its ancestors' intro animations (a card's pop-in lives on
    // the card, a button's scale-in on the button or the popup around it).
    public static bool IsAnimatingAbove(Transform leaf)
    {
        if (leaf == null)
            return false;

        foreach (ShakeGrowImpactAnimation shake in leaf.GetComponentsInParent<ShakeGrowImpactAnimation>(false))
        {
            if (shake.IsPlaying)
                return true;
        }

        foreach (UiTween tween in leaf.GetComponentsInParent<UiTween>(false))
        {
            if (tween.IsPlaying && tween.playType == UiTween.UiPlayType.ONCE)
                return true;
        }

        return false;
    }

    // Longest a popup/card is held back for its animation. Safety net: a tween driven by scaled time
    // never finishes while Time.timeScale is 0, and a locked popup would be a softlock.
    public const float MaxSettleSeconds = 2.5f;

    // Focuses `target` once its intro animation has landed (immediately when it isn't animating), so a
    // controller player never sees focus - or can press Submit - on something that still looks mid-pop.
    // Runs on the target itself, so it ends with it if it is hidden before landing.
    public static void SelectFirstInteractable(Selectable target)
    {
        if (target == null || EventSystem.current == null)
            return;

        if (!IsAnimatingAbove(target.transform))
        {
            EventSystem.current.SetSelectedGameObject(target.gameObject);
            return;
        }

        target.StartCoroutine(SelectWhenSettled(target));
    }

    private static IEnumerator SelectWhenSettled(Selectable target)
    {
        float waited = 0f;
        while (target != null && IsAnimatingAbove(target.transform) && waited < MaxSettleSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (target != null && target.gameObject.activeInHierarchy && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private static GameObject lastFocusInsidePopup;

    /// <summary>
    /// Keeps gamepad/keyboard focus inside the popup that is open, like a modal dialog:
    /// - selection lost (a click that cleared it, an animation, nothing was focused yet): the next stick or
    ///   Submit input focuses the popup's control nearest to the popup's centre - as if the popup itself took focus;
    /// - selection leaked to something behind the popup (HUD, menu): pulled straight back (to where it was in the
    ///   popup, else the nearest control).
    /// A popup with nothing to select leaves focus empty - input never reaches what is behind it.
    /// Call every frame while the popup is open; does nothing while it is still popping in (its own first focus
    /// is pending).
    /// </summary>
    public static void TrapFocusInside(UiPopup popup)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || popup == null || popup.canvasGroup == null || !popup.canvasGroup.interactable)
            return;

        GameObject selected = eventSystem.currentSelectedGameObject;
        bool hasSelection = selected != null && selected.activeInHierarchy;

        if (hasSelection && selected.transform.IsChildOf(popup.transform))
        {
            lastFocusInsidePopup = selected;
            return;
        }

        // Lost (not leaked): only on an actual stick/Submit press, so a mouse user isn't handed a focus ring.
        if (!hasSelection && !(Quantum.GamepadControls.NavigateActive || Quantum.GamepadControls.SubmitPressed))
            return;

        Selectable target = lastFocusInsidePopup != null && lastFocusInsidePopup.transform.IsChildOf(popup.transform)
            ? lastFocusInsidePopup.GetComponent<Selectable>()
            : null;

        if (target == null || !target.IsInteractable() || !target.gameObject.activeInHierarchy)
            target = NearestSelectable(popup.transform);

        eventSystem.SetSelectedGameObject(target != null ? target.gameObject : null);
    }

    /// <summary>The active, interactable Selectable under <paramref name="scope"/> closest to the scope's centre.</summary>
    public static Selectable NearestSelectable(Transform scope)
    {
        var rect = scope as RectTransform;
        Vector2 centre = rect != null ? (Vector2)rect.TransformPoint(rect.rect.center) : (Vector2)scope.position;

        Selectable best = null;
        float bestDistance = float.MaxValue;
        foreach (Selectable candidate in scope.GetComponentsInChildren<Selectable>(false))
        {
            if (!candidate.IsInteractable())
                continue;

            float distance = ((Vector2)candidate.transform.position - centre).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    // Holds `group` non-interactable (no clicks, no Submit, skipped by stick navigation) until everything
    // animating under `root` has settled, then enables it. Returns when done (or after MaxSettleSeconds).
    public static IEnumerator EnableWhenSettled(CanvasGroup group, Transform root)
    {
        group.interactable = false;

        // One frame so tweens that start on enable have begun, then wait for them.
        yield return null;

        float waited = 0f;
        while (IsAnimating(root) && waited < MaxSettleSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        group.interactable = true;
    }
}
