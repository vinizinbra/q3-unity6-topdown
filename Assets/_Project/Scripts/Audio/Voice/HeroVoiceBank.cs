using System;
using System.Collections.Generic;
using NaughtyAttributes;
using Quantum;
using UnityEngine;

// One hero's voice lines, keyed by trigger. WHICH line a hero says lives here; HOW OFTEN any hero
// says anything lives in VoiceLineConfig - separated so re-pacing the game's chattiness doesn't mean
// editing six hero assets.
//
// Lines are plain AudioClips: a voice line is one specific take, so there is nothing for a SoundData
// to add. Several clips on an entry are interchangeable VARIANTS of the same line, picked at random
// so a repeated moment doesn't repeat the same delivery. Group/volume/spatial come once from
// Voice Settings below.
[CreateAssetMenu(fileName = "NewHeroVoiceBank", menuName = "RiftRaiders/Audio/Hero Voice Bank")]
public class HeroVoiceBank : ScriptableObject
{
    // A different line for one specific ally. This is what makes the same moment read differently
    // depending on who else was involved - Max reacting to Pixie versus to Brute.
    [Serializable]
    public class PairOverride
    {
        [Tooltip("Auto-set from the ally and variant count. Exists only to label this row.")]
        public string name;

        [Tooltip("The other hero involved - NOT the speaker. On Max's bank, Pixie means 'what Max says when Pixie is the one involved'.")]
        public HeroId OtherHero;

        [Tooltip("Interchangeable takes of this ally-specific line, picked at random.")]
        public List<AudioClip> Variants = new List<AudioClip>();
    }

    [Serializable]
    public class Entry
    {
        [Tooltip("Auto-set from Trigger. Exists only to label this row.")]
        public string name;

        public VoiceLineTrigger Trigger;

        [Tooltip("Interchangeable takes of this hero's line for this trigger, picked at random - never the same one twice in a row while there is a choice. Used when no ally-specific override applies.")]
        public List<AudioClip> Variants = new List<AudioClip>();

        [Tooltip("Optional ally-specific lines, used INSTEAD of Variants when that hero is the one involved. Only ever consulted for triggers that carry a second player.")]
        public List<PairOverride> PairOverrides = new List<PairOverride>();
    }

    [SerializeField, Tooltip("Which hero this bank belongs to. Also the key an ally's override refers to.")]
    public HeroId hero;

    [SerializeField, Tooltip("This hero's CharacterData asset - the link that lets VoiceDirector turn a live player entity into a HeroId. Without it this hero is never recognised in a match.")]
    public CharacterData characterData;

    [SerializeField, Tooltip("OPTIONAL. Supplies group/volume/pitch/spatial for every clip in this bank - one SoundData with NO clips of its own, acting purely as settings. Leave empty and lines play at full volume on the Voice group, flat.")]
    public SoundData voiceSettings;

    [InfoBox(
        "THIS BANK IS ONLY WHAT *THIS* HERO SAYS.\\n\\n" +
        "A line always lives on the bank of the hero who SPEAKS it. 'Other Hero' in a Pair Override " +
        "names the OTHER participant, never the speaker.\\n\\n" +
        "Example - PIXIE brings MAX his cap. PIXIE did it, so the line lives on HER bank:\\n" +
        "  * PIXIE's bank, AccessoryReturnedToAlly, Pair Override Other Hero = Max\\n" +
        "  (Other Hero = Brute would be a different line, same list.)\\n\\n" +
        "They will not talk over each other: while one hero is speaking ABOUT another, both are held " +
        "silent until that line finishes.\\n\\n" +
        "Several clips on one entry are VARIANTS of the same line - picked at random, never the same " +
        "one twice in a row. They are not a conversation.\\n\\n" +
        "Only these triggers ever involve an ally, so Pair Overrides do nothing on any other: " +
        "AccessoryReturnedToAlly, TeammateDowned, RevivingTeammate, RevivedTeammate.",
        EInfoBoxType.Normal)]
    [SerializeField] public List<Entry> entries = new List<Entry>();

    // Last clip handed out per entry, so a re-roll can avoid an immediate repeat - the single
    // biggest thing that makes a repeated line sound canned.
    [NonSerialized] private readonly Dictionary<List<AudioClip>, AudioClip> _lastPicked = new();

    // The most specific line available: an ally-specific override when the moment involves one,
    // otherwise the hero's generic take. Null means this hero has nothing to say here, which is
    // normal - most heroes won't cover every trigger.
    public AudioClip Resolve(VoiceLineTrigger trigger, HeroId otherHero)
    {
        foreach (Entry entry in entries)
        {
            if (entry == null || entry.Trigger != trigger)
                continue;

            if (otherHero != HeroId.None && entry.PairOverrides != null)
            {
                foreach (PairOverride pair in entry.PairOverrides)
                {
                    if (pair != null && pair.OtherHero == otherHero)
                    {
                        AudioClip specific = Pick(pair.Variants);

                        // Fall through to the generic line rather than going silent when an override
                        // row exists but has no clips in it yet.
                        if (specific != null)
                            return specific;
                    }
                }
            }

            return Pick(entry.Variants);
        }

        return null;
    }

    private AudioClip Pick(List<AudioClip> variants)
    {
        if (variants == null || variants.Count == 0)
            return null;

        if (variants.Count == 1)
            return variants[0];

        _lastPicked.TryGetValue(variants, out AudioClip last);

        // Fold the previous pick out of the range instead of rejection-sampling, so this stays one
        // roll however unlucky it gets.
        int index = UnityEngine.Random.Range(0, variants.Count - 1);
        int lastIndex = last != null ? variants.IndexOf(last) : -1;

        if (lastIndex >= 0 && index >= lastIndex)
            index++;

        AudioClip picked = variants[Mathf.Clamp(index, 0, variants.Count - 1)];
        _lastPicked[variants] = picked;

        return picked;
    }

    [Header("Editor test")]
    [SerializeField, Tooltip("Which entry the Test button plays.")]
    public VoiceLineTrigger testTrigger = VoiceLineTrigger.AccessoryReturnedToAlly;

    [SerializeField, Tooltip("Which ally's override to test. None tests the generic line instead.")]
    public HeroId testAlly = HeroId.None;

    [Button("Test Line")]
    private void TestLine()
    {
#if UNITY_EDITOR
        AudioClip clip = Resolve(testTrigger, testAlly);

        if (clip == null)
        {
            Debug.LogWarning($"{name}: no clip for trigger '{testTrigger}'" +
                (testAlly != HeroId.None ? $" with ally '{testAlly}'" : string.Empty) +
                ". Check the entry exists and has at least one variant.", this);
            return;
        }

        Debug.Log($"[VO] {hero}: {clip.name}", this);
        SoundDataEditorPreview.PlayClip(voiceSettings, clip);
#endif
    }

    [Button("Stop")]
    private void StopLine()
    {
#if UNITY_EDITOR
        SoundDataEditorPreview.Stop(voiceSettings);
#endif
    }

    private void OnValidate()
    {
        foreach (Entry entry in entries)
        {
            if (entry == null)
                continue;

            int variants = entry.Variants != null ? entry.Variants.Count : 0;
            int pairs = entry.PairOverrides != null ? entry.PairOverrides.Count : 0;

            entry.name = pairs > 0
                ? $"{entry.Trigger}  ({variants} variants, +{pairs} ally)"
                : $"{entry.Trigger}  ({variants} variants)";

            if (entry.PairOverrides == null)
                continue;

            foreach (PairOverride pair in entry.PairOverrides)
            {
                if (pair != null)
                    pair.name = $"with {pair.OtherHero}  ({(pair.Variants != null ? pair.Variants.Count : 0)} variants)";
            }
        }
    }
}
