using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gamepad/keyboard focus cue: while this object holds EventSystem focus, a "fake glow" that matches
/// its background shape shows behind it and gently pulses. The glow is a pre-generated sprite grown
/// from the background sprite (see <see cref="FocusOutlineSet"/>), so it follows chamfered corners
/// exactly - one extra quad, no shader.
///
/// Created lazily on first focus, as the last sibling (drawn over neighbours, so tightly packed grids
/// don't cover it). The glow sprite is hollow inside, so it never hides this object's own graphic.
/// It ignores layout, so it is safe inside layout groups.
/// </summary>
public class FocusOutlineWidget : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    private const string LogTag = "FocusOutline";

    [SerializeField, Tooltip("The graphic whose shape gets the glow. Defaults to the Image on this object, else the Selectable's target graphic.")]
    private Image background;

    [SerializeField] private Color color = new Color(0.992f, 0.224f, 0.443f);

    [Header("Pulse")]
    [SerializeField] private bool pulse = true;
    [SerializeField, Range(0f, 1f), Tooltip("How far the outline's alpha dips at the low point of the pulse.")]
    private float pulseDepth = 0.3f;
    [SerializeField, Tooltip("Radians per second.")]
    private float pulseSpeed = 3.5f;

    private RectTransform outline;
    private Image outlineImage;
    private Sprite sourceSprite;
    private int spritePadding;
    private bool focused;
    private bool missingLogged;

    private void Awake()
    {
        ResolveBackground();
    }

    /// <summary>The Image whose sprite/shape is glowed (also used by the Editor tool that generates the glow sprites).</summary>
    public Image ResolveBackground()
    {
        if (background == null)
        {
            background = GetComponent<Image>();

            if (background == null)
            {
                var selectable = GetComponent<Selectable>();
                if (selectable != null)
                    background = selectable.targetGraphic as Image;
            }
        }

        return background;
    }

    public void OnSelect(BaseEventData eventData)
    {
        focused = true;

        if (!EnsureOutline())
            return;

        outline.gameObject.SetActive(true);
        outline.SetAsLastSibling();
        Sync();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        focused = false;

        if (outline != null)
            outline.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        focused = false;

        if (outline != null)
            outline.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        // The outline is a sibling, not a child, so it does not go away with this object.
        if (outline != null)
            Destroy(outline.gameObject);
    }

    private void LateUpdate()
    {
        if (!focused || outline == null)
            return;

        Sync();

        Color c = color;
        if (pulse)
        {
            float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            c.a *= Mathf.Lerp(1f, 1f - pulseDepth, t);
        }
        outlineImage.color = c;
    }

    private bool EnsureOutline()
    {
        if (outline != null)
            return true;

        FocusOutlineSet set = FocusOutlineSet.Instance;
        if (ResolveBackground() == null || set == null || !set.TryGet(background.sprite, out FocusOutlineSet.Entry entry) || entry.focus == null)
        {
            if (!missingLogged)
            {
                missingLogged = true;
                LogHelper.Warn(LogTag, $"No focus outline for '{(background != null && background.sprite != null ? background.sprite.name : "(no sprite)")}' on {name} - run Tools/RiftRaiders/UI/Generate Focus Outlines.");
            }
            return false;
        }

        sourceSprite = entry.source;
        spritePadding = entry.padding;

        var go = new GameObject(name + "_FocusOutline", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
        outline = (RectTransform)go.transform;
        outline.SetParent(transform.parent, false);
        go.GetComponent<LayoutElement>().ignoreLayout = true;

        outlineImage = go.AddComponent<Image>();
        outlineImage.sprite = entry.focus;
        outlineImage.type = entry.focus.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        outlineImage.pixelsPerUnitMultiplier = background.pixelsPerUnitMultiplier;
        outlineImage.raycastTarget = false;
        outlineImage.color = color;
        return true;
    }

    // Mirrors this object's rect, grown by the sprite's padding (works for stretched and fixed anchors alike).
    private void Sync()
    {
        var self = (RectTransform)transform;
        outline.anchorMin = self.anchorMin;
        outline.anchorMax = self.anchorMax;
        outline.pivot = self.pivot;
        outline.localScale = self.localScale;
        outline.localRotation = self.localRotation;
        float pad = CanvasPadding(self);
        outline.offsetMin = self.offsetMin - new Vector2(pad, pad);
        outline.offsetMax = self.offsetMax + new Vector2(pad, pad);
    }

    // The glow sprite is the source sprite plus spritePadding px per side, drawn at the same scale as the
    // background: a sliced image scales its pixels by 1 / (pixelsPerUnit * pixelsPerUnitMultiplier), a simple
    // one by however much its rect stretches the source sprite. Growing the rect by exactly that keeps the
    // glow's inner edge flush with the background's edge.
    private float CanvasPadding(RectTransform self)
    {
        if (outlineImage.type == Image.Type.Sliced)
            return spritePadding / (background.pixelsPerUnit * background.pixelsPerUnitMultiplier);

        float width = sourceSprite != null ? sourceSprite.rect.width : 0f;
        return width > 0f ? spritePadding * self.rect.width / width : spritePadding;
    }
}
