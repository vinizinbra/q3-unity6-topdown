using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Your accessory is NOT on you" marker (see docs/accessory-guard.md, "Missing-accessory feedback"):
// a ghosted copy of the hero's own accessory art with a Rift Pink "!", bobbing by the head for as
// long as the accessory is off the hero but still recoverable (Airborne/Dropped). The worn visual
// simply disappearing was the only cue before, and an absence is exactly what players don't notice.
//
// Same shape as SkillUrgentCountdownWidget: lives under CharacterUiWidget's nameplate rect (follows
// it for free), built at runtime on first need so the shared nameplate prefab needs no re-author and
// an enemy widget never pays for it. CharacterUiWidget decides when it shows; this only animates.
// Everything runs on unscaled time so it keeps moving over a paused sim (level-up, chest).
public class AccessoryMissingWidget : MonoBehaviour
{
    private static readonly Color RiftPink = new Color32(0xFD, 0x39, 0x71, 0xFF);

    private const float AppearDuration = 0.35f;
    private const float DisappearDuration = 0.2f;
    private const float BobAmplitudePx = 5f;
    private const float BobRate = 1.6f;
    private const float AttentionInterval = 1.6f;
    private const float GhostMinAlpha = 0.55f;
    private const float GhostMaxAlpha = 1f;

    private RectTransform _content;
    private Image _icon;
    private Tween _scaleTween;
    private Tween _punchTween;
    private bool _shown;
    private float _shownAt;
    private float _nextAttentionAt;

    private static Sprite s_disc;

    // anchoredOffset is from the nameplate's own centre, in canvas units. icon may be null (hero
    // with no CollectibleSprite authored) - the "!" alone still reads, just bigger. rimColor is the
    // hero's RingColor, tying the token to their foot ring and loot beam.
    public static AccessoryMissingWidget Create(RectTransform parent, TMP_Text fontSource, Vector2 anchoredOffset, float sizePx, Sprite icon, Color rimColor)
    {
        var go = new GameObject("AccessoryMissing", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(sizePx, sizePx);
        rect.anchoredPosition = anchoredOffset;

        // Content is what bobs and scales, so the root keeps the authored position.
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(rect, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;

        Image image = null;

        if (icon != null)
        {
            // A light token behind the art: accessory sprites range from a white cap to a black mask,
            // and the dark ones vanish on the ground without a backing.
            AddDisc(contentRect, "Rim", new Color(rimColor.r, rimColor.g, rimColor.b, 1f), 0f);
            AddDisc(contentRect, "Token", new Color(1f, 1f, 1f, 0.92f), sizePx * 0.07f);

            var iconGo = new GameObject("Ghost", typeof(RectTransform));
            iconGo.transform.SetParent(contentRect, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(sizePx * 0.16f, sizePx * 0.16f);
            iconRect.offsetMax = -iconRect.offsetMin;

            image = iconGo.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        var badgeGo = new GameObject("Badge", typeof(RectTransform));
        badgeGo.transform.SetParent(contentRect, false);
        var badgeRect = (RectTransform)badgeGo.transform;

        // Bottom-right corner of the ghost, like a notification badge; centred when there is no art.
        float badgeSize = icon != null ? sizePx * 0.7f : sizePx * 1.2f;
        badgeRect.anchorMin = badgeRect.anchorMax = icon != null ? new Vector2(0.95f, 0.15f) : new Vector2(0.5f, 0.5f);
        badgeRect.pivot = new Vector2(0.5f, 0.5f);
        badgeRect.sizeDelta = new Vector2(badgeSize, badgeSize);

        var badge = badgeGo.AddComponent<TextMeshProUGUI>();

        if (fontSource != null)
        {
            badge.font = fontSource.font;
            badge.fontSharedMaterial = fontSource.fontSharedMaterial;
        }

        badge.text = "!";
        badge.alignment = TextAlignmentOptions.Center;
        badge.fontStyle = FontStyles.Bold;
        badge.fontSize = badgeSize;
        badge.color = RiftPink;
        badge.raycastTarget = false;
        badge.outlineWidth = 0.3f;
        badge.outlineColor = new Color32(20, 10, 10, 255);

        var widget = go.AddComponent<AccessoryMissingWidget>();
        widget._content = contentRect;
        widget._icon = image;
        contentRect.localScale = Vector3.zero;
        go.SetActive(false);
        return widget;
    }

    private static void AddDisc(RectTransform parent, string name, Color color, float insetPx)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(insetPx, insetPx);
        rect.offsetMax = -rect.offsetMin;

        var disc = go.AddComponent<Image>();
        disc.sprite = ResolveDiscSprite();
        disc.color = color;
        disc.raycastTarget = false;
    }

    // Anti-aliased white circle generated once, so the token needs no art asset.
    private static Sprite ResolveDiscSprite()
    {
        if (s_disc != null)
            return s_disc;

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "AccessoryMissingDisc", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                byte alpha = (byte)(Mathf.Clamp01(radius - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        s_disc = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return s_disc;
    }

    // Edge-triggered: pops in on the transition into shown, shrinks away on the way out.
    public void SetShown(bool shown)
    {
        if (shown == _shown)
            return;

        _shown = shown;
        _scaleTween.Stop();
        _punchTween.Stop();

        if (shown == true)
        {
            gameObject.SetActive(true);
            _shownAt = Time.unscaledTime;
            _nextAttentionAt = _shownAt + AttentionInterval;
            _content.localScale = Vector3.zero;
            _scaleTween = Tween.Scale(_content, Vector3.one, AppearDuration, Ease.OutBack, useUnscaledTime: true);
            return;
        }

        if (gameObject.activeInHierarchy == false)
        {
            HideImmediate();
            return;
        }

        _scaleTween = Tween.Scale(_content, Vector3.zero, DisappearDuration, Ease.InBack, useUnscaledTime: true)
            .OnComplete(this, widget => widget.gameObject.SetActive(false));
    }

    public void HideImmediate()
    {
        _shown = false;
        _scaleTween.Stop();
        _punchTween.Stop();
        _content.localScale = Vector3.zero;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_shown == false)
            return;

        float now = Time.unscaledTime;
        float t = now - _shownAt;

        _content.anchoredPosition = new Vector2(0f, Mathf.Sin(t * BobRate * Mathf.PI * 2f) * BobAmplitudePx);

        if (_icon != null)
        {
            float wave = 0.5f + 0.5f * Mathf.Cos(t * BobRate * Mathf.PI * 2f);
            _icon.color = new Color(1f, 1f, 1f, Mathf.Lerp(GhostMinAlpha, GhostMaxAlpha, wave));
        }

        // A periodic punch on top of the bob - peripheral vision catches a change, not a steady state.
        if (now < _nextAttentionAt || _scaleTween.isAlive)
            return;

        _nextAttentionAt = now + AttentionInterval;
        _punchTween.Stop();
        _content.localScale = Vector3.one;
        _punchTween = Tween.PunchScale(_content, Vector3.one * 0.35f, 0.4f, 2, useUnscaledTime: true);
    }
}
