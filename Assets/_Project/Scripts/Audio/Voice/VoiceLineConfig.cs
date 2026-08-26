using System;
using System.Collections.Generic;
using UnityEngine;

// Playback RULES per trigger - probability, cooldown, priority, audience. Kept in one asset rather
// than scattered across the gameplay systems that raise the triggers, so tuning how talkative the
// game is never means touching gameplay code (spec section 13).
//
// Rules are hero-agnostic on purpose: how OFTEN a hero comments on landing a heavy hit is a pacing
// decision for the whole game, while WHICH line they say is per-hero and lives in HeroVoiceBank.
[CreateAssetMenu(fileName = "VoiceLineConfig", menuName = "RiftRaiders/Audio/Voice Line Config")]
public class VoiceLineConfig : ScriptableObject
{
    [Serializable]
    public class Rule
    {
        // Named `name` on purpose: Unity's default list drawer uses a serialized field with exactly
        // that name as the element's header, so a collapsed rule list reads as the trigger names
        // instead of twenty rows of "Element 0". Kept in sync automatically by OnValidate below -
        // editing it by hand does nothing.
        [Tooltip("Auto-set from Trigger. Exists only so this row is labelled in the list above.")]
        public string name;

        public VoiceLineTrigger Trigger;

        [Range(0f, 1f), Tooltip("Chance this trigger produces a line at all. Below 1 is what keeps a frequent moment from turning its line into a catchphrase - rolled per request, on the View side, so it can never influence gameplay.")]
        public float Probability = 1f;

        [Min(0f), Tooltip("Minimum seconds between two lines from THIS trigger, per speaker. The main anti-repetition control.")]
        public float Cooldown = 15f;

        [Tooltip("Higher priority interrupts a lower-priority line already playing; a lower-priority request is dropped while a higher one is still going.")]
        public VoicePriority Priority = VoicePriority.Normal;

        public VoiceAudience Audience = VoiceAudience.LocalOnly;
    }

    [SerializeField, Tooltip("One row per trigger you want to be audible. A trigger with NO row here never plays - opt-in, so adding a trigger to the enum can't accidentally make the game chattier.")]
    public List<Rule> rules = new List<Rule>();

    [Header("Global limits")]
    [SerializeField, Min(0f), Tooltip("Minimum seconds between ANY two voice lines from the same speaker, regardless of trigger. Stops a burst of different triggers (downed + heavy hit + accessory dropped, all in one moment) from talking over itself.")]
    public float perSpeakerCooldown = 4f;

    [SerializeField, Min(0f), Tooltip("Minimum seconds between ANY two voice lines from ANY speaker. The backstop that keeps four players in co-op from becoming a crowd - the single most important number here.")]
    public float globalCooldown = 2f;

    public float PerSpeakerCooldown => perSpeakerCooldown;
    public float GlobalCooldown => globalCooldown;

    private Dictionary<VoiceLineTrigger, Rule> _lookup;

    // Re-stamped rather than set once at creation, so a rule whose Trigger is changed in the
    // Inspector relabels itself immediately instead of keeping a stale name.
    private void OnValidate()
    {
        foreach (Rule rule in rules)
        {
            if (rule != null)
                rule.name = rule.Trigger.ToString();
        }
    }

    // Null means "this trigger has no rule authored", which callers treat as never plays.
    public Rule GetRule(VoiceLineTrigger trigger)
    {
        if (_lookup == null || _lookup.Count != rules.Count)
        {
            _lookup = new Dictionary<VoiceLineTrigger, Rule>(rules.Count);
            foreach (Rule rule in rules)
            {
                if (rule != null)
                    _lookup[rule.Trigger] = rule;
            }
        }

        return _lookup.TryGetValue(trigger, out Rule found) ? found : null;
    }
}
