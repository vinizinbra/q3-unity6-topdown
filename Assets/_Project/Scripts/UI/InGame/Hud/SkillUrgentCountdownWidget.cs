using PrimeTween;
using TMPro;
using UnityEngine;

// The "about to end" countdown for a hero's TIMED skill (Overdrive, Juggernaut): a big 3 - 2 - 1
// sitting just above the hero's head, between the nameplate and the sprite, where the eyes already
// are in the moment a skill is about to run out. Shown only in the last
// HeroSkillStateUtility.UrgentSeconds; before that the nameplate badge carries the whole seconds.
//
// Lives under CharacterUiWidget and follows it for free (it is a child of the nameplate rect), so it
// needs no entity binding of its own - CharacterUiWidget feeds it the seconds left each LateUpdate.
// Built at runtime on first need (Create) instead of authored on the prefab, so the one shared
// nameplate prefab keeps serving every hero AND every enemy without a re-author, and an enemy widget
// never pays for it.
public class SkillUrgentCountdownWidget : MonoBehaviour
{
    private TMP_Text _label;
    private RectTransform _rect;
    private int _lastShown = -1;
    private Tween _popTween;

    // anchoredOffset is from the nameplate's own centre, in canvas units: negative Y moves it down
    // toward the head. fontSource lends its font so the digits match the rest of the HUD.
    public static SkillUrgentCountdownWidget Create(RectTransform parent, TMP_Text fontSource, Vector2 anchoredOffset, float fontSize)
    {
        var go = new GameObject("SkillUrgentCountdown", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(160f, 90f);
        rect.anchoredPosition = anchoredOffset;

        var label = go.AddComponent<TextMeshProUGUI>();

        if (fontSource != null)
        {
            label.font = fontSource.font;
            label.fontSharedMaterial = fontSource.fontSharedMaterial;
        }

        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.raycastTarget = false;
        label.outlineWidth = 0.3f;
        label.outlineColor = new Color32(20, 10, 10, 255);

        var widget = go.AddComponent<SkillUrgentCountdownWidget>();
        widget._label = label;
        widget._rect = rect;
        go.SetActive(false);
        return widget;
    }

    // Painted once at creation (the hero's ring colour, or white) - the dark outline keeps either
    // readable over any ground.
    public void SetColor(Color color)
    {
        _label.color = color;
    }

    // Whole seconds, rounded up - the classic 3 - 2 - 1, never a 0 while the skill still runs.
    // Each new number punches in; unscaled time so it keeps animating over a paused sim.
    public void Show(float secondsLeft)
    {
        int shown = Mathf.Max(1, Mathf.CeilToInt(secondsLeft));

        if (gameObject.activeSelf == false)
            gameObject.SetActive(true);

        if (shown == _lastShown)
            return;

        _lastShown = shown;
        _label.text = shown.ToString();

        if (_popTween.isAlive)
            _popTween.Stop();

        _rect.localScale = Vector3.one;
        _popTween = Tween.PunchScale(_rect, Vector3.one * 0.6f, 0.4f, 2, useUnscaledTime: true);
    }

    public void Hide()
    {
        _lastShown = -1;

        if (_popTween.isAlive)
            _popTween.Stop();

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
