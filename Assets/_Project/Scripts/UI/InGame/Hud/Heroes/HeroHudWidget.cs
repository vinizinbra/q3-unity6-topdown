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
// pieces (root/slider/text/complete badge) may be unassigned on a given prefab, and the component
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
    // already uses, just with a fill/value/complete triple instead of a lone timer string. root is
    // whatever the Inspector wires as that piece's visual (a stack row, a slider, a badge), shown
    // only while its own data resolves; slider/valueText/completeObject are each independently
    // optional, so the same class covers a bare slider, a bare "3/5" row, or both at once.
    [System.Serializable]
    protected class Section
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Slider slider;
        [SerializeField] private TMP_Text valueText;
        [SerializeField, Tooltip("Optional - toggled on once this section's own resource is full/ready (Juggernaut Charged, max Rage, a Resonance pulse about to fire, Scrap at its free-cast threshold). Author the badge/shine itself on this object in the Editor rather than here in code.")]
        private GameObject completeObject;

        public void Hide()
        {
            SetShown(root, false);
            SetShown(completeObject, false);
        }

        public void Show(float fill, string value, bool complete = false)
        {
            SetShown(root, true);

            if (slider != null)
                slider.value = fill;

            if (valueText != null)
                valueText.text = value;

            SetShown(completeObject, complete);
        }
    }
}
