using Photon.Deterministic;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Base for the per-hero resource readouts (Brute/Max/Zara/Lux) that live under CharacterUiWidget -
// one widget per HERO, not one per resource, so everything a given hero needs to read at a glance
// (gauge + stack count + how long the channel has left) is authored and gated together.
//
// Deliberately a plain MonoBehaviour driven by the CharacterUiWidget above it (Refresh, once per
// LateUpdate, handed the frame that widget already read) rather than a QuantumGlobalMonoBehaviour
// with its own QUpdate: these only ever exist as a child of a character's own widget, which already
// knows exactly which entity it follows, so there is nothing to bind - no MyLocalPlayer slot
// binding, no autoBindLocalPlayerOne/DisableAutoBind pair, no party-HUD hand-off. That's also what
// makes them work for a teammate's or a remote player's widget for free, where the old local-slot-
// bound widgets could only ever show player 1.
//
// Every section is optional twice over, same convention CharacterUiWidget itself uses: its own
// pieces (root/fill image/value text) may be unassigned on a given prefab, and the component
// behind it may be absent on a given entity (an enemy has none of these, a Brute who isn't
// channeling has no JuggernautCharge). Each section hides itself rather than the widget assuming a
// loadout.
public abstract class HeroHudWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Optional wrapper toggled with the whole hero group. Left unassigned - the usual case, since every hero widget sits on the same shared overlay GameObject - only the individual sections below toggle.")]
    private GameObject root;

    // Called by the CharacterUiWidget this widget lives under, every LateUpdate, on the frame that
    // widget already resolved - see CharacterUiWidget.UpdateHeroWidgets.
    public void Refresh(Frame frame, EntityRef entity)
    {
        bool shown = frame.Exists(entity) && TryRefresh(frame, entity);

        if (shown == false)
            HideSections();

        SetShown(root, shown);
    }

    // Fill in whatever this hero shows, or return false when the followed entity isn't that hero /
    // isn't currently carrying the resource at all - the base then hides every section for free.
    protected abstract bool TryRefresh(Frame frame, EntityRef entity);

    protected abstract void HideSections();

    // Shared "which slot is currently channeling this hero's own skill, and how long does it have
    // left" resolver - both Juggernaut and Overdrive seed SkillSlot.StateTimer with their asset's
    // own Duration at Begin and count it down to 0 (see JuggernautSkillData/BerserkSkillData.Tick),
    // so remaining/duration is a real end-of-skill countdown, not an approximation. Both slots are
    // checked because which one carries a given SkillData is per-hero prototype config, not a
    // guarantee.
    protected static bool TryGetActiveSkill<T>(Frame frame, EntityRef entity, out T skill, out FP remaining, out FP duration) where T : SkillData
    {
        skill = null;
        remaining = FP._0;
        duration = FP._0;

        if (frame.TryGet<CharacterSkills>(entity, out var skills) == false)
            return false;

        return TryReadSlot(frame, skills.HeroSkill, ref skill, ref remaining, ref duration)
            || TryReadSlot(frame, skills.DashSkill, ref skill, ref remaining, ref duration);
    }

    private static bool TryReadSlot<T>(Frame frame, SkillSlot slot, ref T skill, ref FP remaining, ref FP duration) where T : SkillData
    {
        if (slot.State != SkillState.Active || slot.Skill == default)
            return false;

        skill = frame.FindAsset(slot.Skill) as T;

        if (skill == null)
            return false;

        remaining = slot.StateTimer;
        duration = skill.GetActiveDuration();
        return true;
    }

    protected static string FormatSeconds(FP remaining)
    {
        return $"{Mathf.Max(0f, remaining.AsFloat):F1}s";
    }

    private static void SetShown(GameObject go, bool shown)
    {
        if (go == null || go.activeSelf == shown)
            return;

        go.SetActive(shown);
    }

    // One authored row or gauge - same nested-serializable shape CharacterUiWidget.StatusIndicator
    // already uses, just with a fill/value pair instead of a lone timer string. root is whatever the
    // Inspector wires as that piece's visual (a stack row, a bar, a badge), shown only while its own
    // data resolves; fillImage/valueText are each independently optional, so the same class covers a
    // bare bar, a bare "3/5" row, or both at once.
    //
    // The gauge is an Image.fillAmount, not a Slider - a Slider is three GameObjects and a
    // fill-rect layout pass for something that only ever displays a number, and its Fill child has
    // to be re-anchored by the Slider every frame. One Filled Image does the same job with the
    // artist in full control of the graphic (radial gauges, angled bars, sliced sprites).
    //
    // "Full" no longer toggles a separate badge object either - the section glows instead, pulsing
    // whatever color was AUTHORED on the fill/text rather than tweening toward white, so a full bar
    // still reads as that resource's own color. See ResolveGlowColor.
    [System.Serializable]
    protected class Section
    {
        // A brightened target this close to the base color isn't worth pulsing toward - see
        // ResolveGlowColor, which breathes darker instead when the authored color is already maxed.
        private const float MinGlowDelta = 0.05f;

        [SerializeField] private GameObject root;
        [SerializeField, Tooltip("The gauge graphic, driven via Image.fillAmount - its Image Type MUST be set to Filled in the Inspector or nothing moves. Optional; leave unassigned for a text-only row.")]
        private Image fillImage;
        [SerializeField] private TMP_Text valueText;

        [Header("Full Glow")]
        [SerializeField, Tooltip("On: the fill (and its value text) pulse while this section's own resource is full/ready - Rage at max, Juggernaut Charged, Zara at Max Flow, Scrap at its free-cast threshold. Off: full looks exactly like any other value.")]
        private bool glowWhenFull = true;
        [SerializeField, Tooltip("Optional explicit color to pulse toward. Leave alpha at 0 (the default) to derive it from whatever color is authored on the fill/text instead - see glowIntensity.")]
        private Color glowColor = new Color(0f, 0f, 0f, 0f);
        [SerializeField, Tooltip("Only used when glowColor is left unset. Multiplies the AUTHORED color's brightness, keeping its hue and saturation exactly - so a full bar pulses as a brighter version of its own color instead of washing out toward white."), Range(1.05f, 3f)]
        private float glowIntensity = 1.5f;
        [SerializeField, Tooltip("Seconds for one full pulse (base -> glow -> base). 0 pins the section at its glow color with no animation."), Min(0f)]
        private float glowCycleDuration = 0.8f;

        // The authored colors, captured the first time this section is driven and never overwritten
        // - every glow frame lerps FROM these and every non-glow frame restores them exactly, so a
        // pulse can't accumulate into a permanently washed-out bar.
        private Color _baseFillColor;
        private Color _baseTextColor;
        private bool _baseColorsCaptured;

        public void Hide()
        {
            SetShown(root, false);
            RestoreBaseColors();
        }

        public void Show(float fill, string value, bool full = false)
        {
            SetShown(root, true);
            CaptureBaseColors();

            if (fillImage != null)
                fillImage.fillAmount = Mathf.Clamp01(fill);

            if (valueText != null)
                valueText.text = value;

            if (full && glowWhenFull)
                ApplyGlow();
            else
                RestoreBaseColors();
        }

        // Runs before anything below ever writes a color, so what's captured is always the color the
        // artist authored on the prefab, not a mid-pulse one.
        private void CaptureBaseColors()
        {
            if (_baseColorsCaptured)
                return;

            if (fillImage != null)
                _baseFillColor = fillImage.color;

            if (valueText != null)
                _baseTextColor = valueText.color;

            _baseColorsCaptured = true;
        }

        // Both setters early-out on an unchanged value internally, so re-assigning the same base
        // color every LateUpdate costs nothing and never dirties the canvas.
        private void RestoreBaseColors()
        {
            if (_baseColorsCaptured == false)
                return;

            if (fillImage != null)
                fillImage.color = _baseFillColor;

            if (valueText != null)
                valueText.color = _baseTextColor;
        }

        // Driven straight off unscaled time rather than a coroutine/tween: Show is already called
        // once per LateUpdate by the CharacterUiWidget above (see HeroHudWidget.Refresh), so there
        // is nothing to start, stop, or leak - the pulse simply stops being evaluated the moment the
        // resource stops being full, and RestoreBaseColors puts the authored color straight back.
        // Unscaled so it keeps breathing while the sim is paused (a Level-Up screen, the Boss
        // reveal), same choice every other HUD animation here makes.
        private void ApplyGlow()
        {
            float t = glowCycleDuration > 0f
                ? 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * (2f * Mathf.PI / glowCycleDuration))
                : 1f;

            if (fillImage != null)
                fillImage.color = Color.Lerp(_baseFillColor, ResolveGlowColor(_baseFillColor), t);

            if (valueText != null)
                valueText.color = Color.Lerp(_baseTextColor, ResolveGlowColor(_baseTextColor), t);
        }

        // The whole point of the base-color capture: brightening happens in HSV with hue and
        // saturation held fixed, so a red bar pulses red and a green bar pulses green - lerping
        // toward Color.white (the obvious version of this) is exactly what turns a full bar into a
        // washed-out whitish one.
        //
        // A color authored at full brightness (v == 1, which most punchy HUD colors are) has nowhere
        // brighter to go, so it breathes DARKER instead - same visible pulse, still exactly its own
        // color, no white anywhere.
        private Color ResolveGlowColor(Color baseColor)
        {
            if (glowColor.a > 0f)
                return glowColor;

            Color.RGBToHSV(baseColor, out float h, out float s, out float v);

            float brightened = Mathf.Min(1f, v * glowIntensity);
            float glowValue = brightened - v >= MinGlowDelta ? brightened : v / glowIntensity;

            Color result = Color.HSVToRGB(h, s, Mathf.Clamp01(glowValue));
            result.a = baseColor.a;
            return result;
        }
    }
}
