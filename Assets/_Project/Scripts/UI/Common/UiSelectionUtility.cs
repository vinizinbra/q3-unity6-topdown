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

    // extraDelay math (e.g. ChooseWindow's per-card stagger) is already baked into whichever Play()
    // call started target's own ShakeGrowImpactAnimation, if any - Finished fires at the real end of
    // that, so this doesn't need to know the timing itself, just whether to wait at all.
    public static void SelectFirstInteractable(Selectable target)
    {
        if (target == null || EventSystem.current == null)
            return;

        ShakeGrowImpactAnimation anim = target.GetComponentInParent<ShakeGrowImpactAnimation>();

        if (anim == null)
        {
            EventSystem.current.SetSelectedGameObject(target.gameObject);
            return;
        }

        void OnFinished()
        {
            anim.Finished -= OnFinished;

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        anim.Finished += OnFinished;
    }
}
