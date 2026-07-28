namespace Quantum
{
    // For a skill whose entire effect is its Actions list - nothing bespoke to resolve itself
    // (unlike Dash's travel, Projectile's launch, or a channel like Berserk/Juggernaut). Begin
    // resolves immediately, so every Begin-phase action fires on cast with no further ticking -
    // e.g. Lux's sentry gun, whose whole skill is "spawn this prototype in front of me"
    // (SpawnEntitySkillAction, Phase = Begin).
    public unsafe partial class InstantSkillData : SkillData
    {
        public override bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)
        {
            return true;
        }
    }
}
