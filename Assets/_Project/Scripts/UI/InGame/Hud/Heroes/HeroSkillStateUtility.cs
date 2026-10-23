using Photon.Deterministic;
using Quantum;
using UnityEngine;

// What a hero's skill/passive looks like to the view, resolved ONE way for every hero so the foot
// ring (MovementRingView) and the nameplate widgets can't disagree about what is on or for how long.
//
// Three shapes, because the heroes really do come in these shapes:
//  - TIMED skill: Max's Overdrive and Brute's Juggernaut drain SkillSlot.StateTimer against the
//    skill's GetActiveDuration() - a real countdown, so SecondsLeft >= 0 and Fill drains 1 -> 0.
//  - PASSIVE METER: Zara's Flow State (Progress 0..1) and Lux's Scavenge (ScrapStacks of
//    StacksRequired) are meters that are ALWAYS on while the passive exists - Fill is the meter and
//    Full is the payoff moment (Flow active / free cast banked). No clock, SecondsLeft is -1.
//
// Everything is read straight off the frame it is handed (the sim stays authoritative, the UI never
// derives gameplay state), same convention as the rest of the hero HUD.
public struct HeroSkillState
{
    // Show the indicator at all: a timed skill is running, or the hero has a passive meter.
    public bool Active;

    // The skill is ON / the meter is at its payoff: Overdrive running, Flow active, free cast banked.
    public bool Full;

    // Timed: remaining / duration (drains). Passive meter: the meter's own 0..1.
    public float Fill;

    // -1 when the state has no countdown (Flow, Scavenge).
    public float SecondsLeft;

    public Sprite Icon;

    public bool IsTimed => SecondsLeft >= 0f;

    // The "about to end" window - a timed state only; a passive never expires on its own.
    public bool IsUrgent => IsTimed && SecondsLeft <= HeroSkillStateUtility.UrgentSeconds;
}

public static class HeroSkillStateUtility
{
    // Under this the countdown switches to decimals and the badge/ring start warning.
    public const float UrgentSeconds = 3f;

    public static bool TryGet(Frame frame, EntityRef entity, out HeroSkillState state)
    {
        state = default;
        state.SecondsLeft = -1f;

        if (frame.Exists(entity) == false)
            return false;

        // Passives first: the component is hero-specific, so a hero that owns one is showing THAT
        // meter on its ring, whatever else its Hero Skill happens to do.
        if (frame.TryGet<ZaraFlow>(entity, out var flow))
        {
            state.Active = true;
            state.Fill = Mathf.Clamp01(flow.Progress.AsFloat);
            state.Full = flow.IsActive;
            return true;
        }

        // Scavenge: stacks hold at the threshold until the free cast is spent, so a full meter IS the
        // banked state.
        if (frame.TryGet<LuxScrapCollector>(entity, out var scrap) && scrap.StacksRequired > 0)
        {
            state.Active = true;
            state.Fill = Mathf.Clamp01(scrap.ScrapStacks / (float)scrap.StacksRequired);
            state.Full = scrap.ScrapStacks >= scrap.StacksRequired;
            return true;
        }

        return TryGetTimed(frame, entity, ref state);
    }

    // Any hero/dash slot channeling a skill with a real duration - which today is exactly Overdrive
    // and Juggernaut, but a future timed skill picks this up for free. Both slots are checked for
    // the same reason HeroHudWidget.TryGetActiveSkill checks them: which slot carries a given
    // SkillData is prototype config, not a guarantee.
    private static bool TryGetTimed(Frame frame, EntityRef entity, ref HeroSkillState state)
    {
        if (frame.TryGet<CharacterSkills>(entity, out var skills) == false)
            return false;

        return TryReadSlot(frame, skills.HeroSkill, ref state) || TryReadSlot(frame, skills.DashSkill, ref state);
    }

    private static bool TryReadSlot(Frame frame, SkillSlot slot, ref HeroSkillState state)
    {
        if (slot.State != SkillState.Active || slot.Skill == default)
            return false;

        SkillData skill = frame.FindAsset(slot.Skill);

        if (skill == null)
            return false;

        FP duration = skill.GetActiveDuration();

        if (duration <= FP._0)
            return false;

        float remaining = Mathf.Max(0f, slot.StateTimer.AsFloat);

        state.Active = true;
        state.Full = true;
        state.SecondsLeft = remaining;
        state.Fill = Mathf.Clamp01(remaining / duration.AsFloat);
        state.Icon = skill.Icon;
        return true;
    }
}
