using System.Collections.Generic;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Draw-call optimizer for a UI subtree whose text is interleaved with images.
//
// A Canvas can only batch consecutive graphics that share a material. TMP text uses its own font
// atlas material, so every text sitting between two images splits the sprite batch in half - a
// widget authored as [bar][label][bar][label][icon][timer] costs one draw call per run instead of
// two. This component hoists every TMP_Text under it into a single "TextGroup" node parented
// directly to the Canvas, leaving an empty placeholder RectTransform in each text's original slot,
// then mirrors the placeholder's transform onto the hoisted text every LateUpdate. All text in the
// Canvas ends up contiguous, so the images collapse into one run and the text into another.
//
// Ported from the jelly-upgrade-q3 project (TextMeshProOptimizer/PgTextMeshProOptimizer, which had
// drifted into two divergent copies), rebuilt as ONE implementation and hardened for the things
// this codebase does that that one never had to survive:
//
//  - Widgets are created and destroyed constantly (EnemyUiWidgetManager Instantiates/Destroys one
//    per enemy). A hoisted text is no longer a child of the widget, so destroying the widget would
//    strand it on screen forever - this component owns its hoisted texts and destroys them itself.
//  - Widgets are pooled and toggled (SetActive), and code toggles individual labels directly
//    (CharacterUiWidget.SetShown). A hoisted text no longer responds to either, so visibility is
//    mirrored off the placeholder and SetActive below redirects a toggle onto the placeholder.
//  - Widgets fade via CanvasGroup (DamageNumberUiWidget). A hoisted text escapes that CanvasGroup,
//    so the alpha of every group it used to sit under is recombined onto it each frame.
//
// Put it on the ROOT of a widget that mixes text and images; it claims every TMP_Text beneath it.
// It is deliberately opt-in per prefab rather than global - see the mask/nested-Canvas guards in
// Hoist for the cases it refuses to touch.
[DisallowMultipleComponent]
public class TextBatchOptimizer : MonoBehaviour
{
    private const string TextGroupName = "TextGroup";

    // Maps a hoisted text's GameObject to the placeholder left behind in its original slot, so
    // SetActive can redirect a caller that only knows about the text (see CharacterUiWidget.SetShown).
    private static readonly Dictionary<GameObject, GameObject> Redirects = new Dictionary<GameObject, GameObject>();

    [SerializeField, Tooltip("Recombines the alpha of every CanvasGroup the text used to sit under onto the hoisted copy. Leave on unless this widget provably has no CanvasGroup above its text - without it, a hoisted text ignores the fade its own widget is playing.")]
    private bool mirrorAlpha = true;

    private readonly List<HoistedText> _hoisted = new List<HoistedText>();
    private bool _hasHoisted;

    // Hoisting waits for OnEnable rather than Awake so the widget is already parented under its
    // real HUD slot - the Canvas the TextGroup belongs to can't be resolved before that. Managers
    // here Instantiate straight into widgetParent, so the very first OnEnable is already in place.
    private void OnEnable()
    {
        if (_hasHoisted == false)
            Hoist();

        TextBatchOptimizerManager.Register(this);

        // Pulls the text back to this widget's current placement immediately. OnDisable switched it
        // off, and the driver's own pass doesn't run until later this frame.
        Sync();
    }

    private void OnDisable()
    {
        TextBatchOptimizerManager.Unregister(this);

        // A pooled widget is switched off, not destroyed, and its hoisted text is no longer a
        // child - nothing else would hide it, and Sync has stopped running by now.
        foreach (HoistedText hoisted in _hoisted)
        {
            if (hoisted.Text != null)
                hoisted.Text.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        TextBatchOptimizerManager.Unregister(this);

        foreach (HoistedText hoisted in _hoisted)
        {
            if (hoisted.Text == null)
                continue;

            Redirects.Remove(hoisted.Text.gameObject);
            Destroy(hoisted.Text.gameObject);
        }

        _hoisted.Clear();
    }

    // Toggles a component that may have been hoisted out of its original slot. Call this instead of
    // gameObject.SetActive on anything a TextBatchOptimizer might have claimed: switching a hoisted
    // text off directly does nothing, because the next Sync restores it from its placeholder.
    public static void SetActive(GameObject target, bool shown)
    {
        if (target == null)
            return;

        if (Redirects.TryGetValue(target, out GameObject placeholder) && placeholder != null)
            target = placeholder;

        if (target.activeSelf != shown)
            target.SetActive(shown);
    }

    private void Hoist()
    {
        _hasHoisted = true;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            LogHelper.Warn("TextBatch", "No Canvas above this widget - text stays where it is.", this);
            return;
        }

        RectTransform textGroup = ResolveTextGroup(canvas);

        // Collected up front: the loop reparents as it goes, which would invalidate a live search.
        var texts = new List<TMP_Text>(GetComponentsInChildren<TMP_Text>(true));

        foreach (TMP_Text text in texts)
        {
            if (CanHoist(text, canvas))
                HoistOne(text, textGroup);
        }
    }

    // Refuses the cases where moving the text out of its parent would change what the player sees,
    // rather than silently breaking them the way a blanket hoist would.
    private bool CanHoist(TMP_Text text, Canvas canvas)
    {
        // TextMeshPro (3D, MeshRenderer) isn't part of Canvas batching at all.
        if (text is TextMeshProUGUI == false)
            return false;

        // The root itself has no slot to leave a placeholder in - it IS the slot.
        if (text.transform == transform)
            return false;

        for (Transform current = text.transform.parent; current != null && current != canvas.transform; current = current.parent)
        {
            // Escaping a mask would let the text draw outside the region that was clipping it.
            if (current.GetComponent<Mask>() != null || current.GetComponent<RectMask2D>() != null)
            {
                LogHelper.Warn("TextBatch", $"'{text.name}' sits under a mask ({current.name}) - left in place, hoisting it would let it draw outside the clip.", this);
                return false;
            }

            // A nested Canvas already isolates its own batching; hoisting out of it would undo that.
            if (current.GetComponent<Canvas>() != null)
                return false;
        }

        return true;
    }

    private void HoistOne(TMP_Text text, RectTransform textGroup)
    {
        var textRect = (RectTransform)text.transform;
        Transform originalParent = textRect.parent;

        var placeholderObject = new GameObject($"{text.name} (Text Slot)", typeof(RectTransform));
        var placeholder = (RectTransform)placeholderObject.transform;

        // Same parent AND same sibling index, so any LayoutGroup above still sizes and orders this
        // slot exactly as it did when the text itself lived here.
        placeholder.SetParent(originalParent, false);
        placeholder.SetSiblingIndex(textRect.GetSiblingIndex());
        CopyRect(textRect, placeholder);

        // Carries the label's authored on/off state across with it. Setup runs before the widget is
        // ever enabled (CharacterUiWidget hides an unnamed enemy's nameText there), so by the time
        // this hoist happens the text may already be switched off - without this the placeholder
        // would come up active and the first Sync would put the label back on screen.
        placeholderObject.SetActive(textRect.gameObject.activeSelf);

        List<CanvasGroup> sourceGroups = mirrorAlpha ? CollectCanvasGroups(originalParent, textGroup) : null;

        var hoisted = new HoistedText
        {
            Text = text,
            Placeholder = placeholder,
            // Only worth a component when something above the text actually fades it - most widgets
            // here have no CanvasGroup at all, and a group pinned at 1 every frame buys nothing.
            Group = sourceGroups == null ? null : EnsureCanvasGroup(text.gameObject),
            SourceGroups = sourceGroups,
        };

        textRect.SetParent(textGroup, false);

        // Driven by world position from here on, so the anchor setup just has to be neutral - the
        // pivot stays as authored so the placeholder's own position lines up exactly.
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);

        Redirects[text.gameObject] = placeholderObject;
        _hoisted.Add(hoisted);

        Sync(hoisted);
    }

    // Called by TextBatchOptimizerManager after every widget has moved for the frame.
    public void Sync()
    {
        for (int i = _hoisted.Count - 1; i >= 0; i--)
        {
            HoistedText hoisted = _hoisted[i];

            if (hoisted.Text == null || hoisted.Placeholder == null)
            {
                _hoisted.RemoveAt(i);
                continue;
            }

            Sync(hoisted);
        }
    }

    private static void Sync(HoistedText hoisted)
    {
        var textRect = (RectTransform)hoisted.Text.transform;
        RectTransform placeholder = hoisted.Placeholder;

        bool shown = placeholder.gameObject.activeInHierarchy;
        if (hoisted.Text.gameObject.activeSelf != shown)
            hoisted.Text.gameObject.SetActive(shown);

        if (shown == false)
            return;

        textRect.position = placeholder.position;
        textRect.rotation = placeholder.rotation;
        textRect.sizeDelta = placeholder.rect.size;

        // lossyScale on both sides, so the Canvas' own scale factor - already baked into the
        // TextGroup this text now lives under - isn't applied a second time.
        textRect.localScale = DivideScale(placeholder.lossyScale, textRect.parent.lossyScale);

        if (hoisted.Group != null)
            hoisted.Group.alpha = ResolveSourceAlpha(hoisted.SourceGroups);
    }

    private RectTransform ResolveTextGroup(Canvas canvas)
    {
        Transform host = ResolveGroupHost(canvas);

        Transform existing = host.Find(TextGroupName);
        if (existing != null)
        {
            // Last sibling, so text always draws over the images it was pulled out from between.
            existing.SetAsLastSibling();
            return (RectTransform)existing;
        }

        var groupObject = new GameObject(TextGroupName, typeof(RectTransform));
        var group = (RectTransform)groupObject.transform;

        group.SetParent(host, false);
        group.anchorMin = Vector2.zero;
        group.anchorMax = Vector2.one;
        group.offsetMin = Vector2.zero;
        group.offsetMax = Vector2.zero;
        group.SetAsLastSibling();

        return group;
    }

    // The group is parked at the end of whichever top-level Canvas child this widget lives under,
    // NOT at the end of the Canvas itself. Everything in this project shares one Canvas - the HUD
    // and every window alike - so a group pinned as the Canvas' last child would draw hoisted text
    // over any window that opens after it in the hierarchy (ChooseWindow, BossWindow, popups).
    // Sitting inside the HUD's own top-level node keeps the text above the images it was pulled out
    // from between, and still underneath anything that legitimately covers the HUD.
    private Transform ResolveGroupHost(Canvas canvas)
    {
        Transform current = transform;

        while (current.parent != null && current.parent != canvas.transform)
            current = current.parent;

        // Either the walk never reached the Canvas, or this widget IS a top-level Canvas child - in
        // which case there is no enclosing node to hide inside and the group belongs on the Canvas.
        if (current == transform || current.parent != canvas.transform)
            return canvas.transform;

        return current;
    }

    private static CanvasGroup EnsureCanvasGroup(GameObject target)
    {
        var group = target.GetComponent<CanvasGroup>();
        if (group == null)
            group = target.AddComponent<CanvasGroup>();

        // The text is a readout, never a click target - and it now lives outside whatever widget
        // used to own its raycasting.
        group.blocksRaycasts = false;
        group.interactable = false;

        return group;
    }

    // Every CanvasGroup between the text's original slot and the Canvas, i.e. exactly the ones that
    // used to fade it and no longer can.
    private static List<CanvasGroup> CollectCanvasGroups(Transform from, Transform stopBefore)
    {
        List<CanvasGroup> groups = null;

        for (Transform current = from; current != null && current != stopBefore; current = current.parent)
        {
            var group = current.GetComponent<CanvasGroup>();
            if (group == null)
                continue;

            groups ??= new List<CanvasGroup>();
            groups.Add(group);
        }

        return groups;
    }

    private static float ResolveSourceAlpha(List<CanvasGroup> groups)
    {
        if (groups == null)
            return 1f;

        float alpha = 1f;
        foreach (CanvasGroup group in groups)
        {
            if (group != null)
                alpha *= group.alpha;
        }

        return alpha;
    }

    private static Vector3 DivideScale(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            Mathf.Approximately(divisor.x, 0f) ? value.x : value.x / divisor.x,
            Mathf.Approximately(divisor.y, 0f) ? value.y : value.y / divisor.y,
            Mathf.Approximately(divisor.z, 0f) ? value.z : value.z / divisor.z);
    }

    private static void CopyRect(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta = from.sizeDelta;
        to.localScale = from.localScale;
        to.localRotation = from.localRotation;
    }

    private class HoistedText
    {
        public TMP_Text Text;
        public RectTransform Placeholder;
        public CanvasGroup Group;
        public List<CanvasGroup> SourceGroups;
    }
}
