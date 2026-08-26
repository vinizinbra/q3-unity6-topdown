using Quantum;
using UnityEngine;

// Everything presentation needs to decide whether a voice line plays, which one, and who hears it.
//
// A PRESENTATION type, not a simulation one, and deliberately so: the simulation's job is to say
// what happened (it already does, via its existing events), never to choose or play audio. This is
// assembled View-side by VoiceDirector from those events and never enters simulation state.
public readonly struct VoiceLineRequest
{
    // Whose line this is. The hero who speaks, not necessarily the entity the event was "about".
    public readonly EntityRef Speaker;
    public readonly HeroId SpeakerHero;

    public readonly VoiceLineTrigger Trigger;

    // The other player the moment involves, if any - the teammate who went down, the ally who
    // fetched your hat. EntityRef.None when the moment involves only the speaker.
    public readonly EntityRef RelatedPlayer;

    // The other player's hero, carried alongside RelatedPlayer so a pair-specific exchange
    // (Pixie returning MAX's hat vs returning BRUTE's) can be resolved without another lookup.
    public readonly HeroId RelatedHero;

    // The upgrade this is about, for SkillUpgradeAcquired/Maxed. Presentation can resolve it to a
    // name/rank for a line or a subtitle. Default for every other trigger.
    public readonly AssetRef<UpgradeData> Upgrade;

    // Free-form, trigger-specific: the accessory's remaining durability, an upgrade's new rank, or
    // whatever a HeroExceptionalEvent wants to distinguish itself by. Beats growing the trigger enum
    // once per hero mechanic.
    public readonly int ContextValue;

    public VoiceLineRequest(
        VoiceLineTrigger trigger,
        EntityRef speaker,
        HeroId speakerHero,
        EntityRef relatedPlayer = default,
        HeroId relatedHero = HeroId.None,
        AssetRef<UpgradeData> upgrade = default,
        int contextValue = 0)
    {
        Trigger = trigger;
        Speaker = speaker;
        SpeakerHero = speakerHero;
        RelatedPlayer = relatedPlayer;
        RelatedHero = relatedHero;
        Upgrade = upgrade;
        ContextValue = contextValue;
    }

    // True when this is an interaction BETWEEN two different heroes - the shape a pair dialogue
    // needs. Checked on the heroes rather than the entities so it can't be fooled by a player
    // interacting with their own second local slot.
    public bool IsHeroPair => RelatedHero != HeroId.None && RelatedHero != SpeakerHero;

    public override string ToString()
    {
        string related = RelatedHero != HeroId.None ? $" Related: {RelatedHero}" : string.Empty;
        string context = ContextValue != 0 ? $" Context: {ContextValue}" : string.Empty;
        return $"Trigger: {Trigger} Speaker: {SpeakerHero}{related}{context}";
    }
}
