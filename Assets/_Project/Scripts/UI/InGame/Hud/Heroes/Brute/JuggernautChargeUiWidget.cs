using Quantum;

// JuggernautCharge's SkillProgressUiWidget: unlike RageOverdrive, MaxCharge lives on
// JuggernautSkillData (the asset), not the component, so the active skill has to be resolved first -
// same duplicated-per-view resolver shape as BerserkFxView/JuggernautView, since which slot ends up
// carrying JuggernautSkillData is per-hero prototype config, not guaranteed. "Complete" here means
// Charged (ChargePoints reached MaxCharge), mirroring JuggernautView's own state check.
public class JuggernautChargeUiWidget : SkillProgressUiWidget
{
    protected override bool TryGetProgress(Frame frame, EntityRef entity, out int current, out int max, out bool complete)
    {
        current = 0;
        max = 0;
        complete = false;

        if (frame.Has<CharacterSkills>(entity) == false || frame.Has<JuggernautCharge>(entity) == false)
            return false;

        JuggernautSkillData skill = ResolveActiveSkill(frame, frame.Get<CharacterSkills>(entity));

        if (skill == null)
            return false;

        JuggernautCharge charge = frame.Get<JuggernautCharge>(entity);
        current = charge.ChargePoints;
        max = skill.MaxCharge;
        complete = charge.ChargePoints >= skill.MaxCharge;
        return true;
    }

    private static JuggernautSkillData ResolveActiveSkill(Frame f, CharacterSkills skills)
    {
        if (TryResolveActiveSkill(f, skills.DashSkill, out var skill) == true)
            return skill;

        if (TryResolveActiveSkill(f, skills.HeroSkill, out skill) == true)
            return skill;

        return null;
    }

    private static bool TryResolveActiveSkill(Frame f, SkillSlot slot, out JuggernautSkillData skill)
    {
        skill = null;

        if (slot.State != SkillState.Active)
            return false;

        skill = f.FindAsset(slot.Skill) as JuggernautSkillData;
        return skill != null;
    }
}
