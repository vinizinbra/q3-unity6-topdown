namespace Quantum
{
    // Dash Ascension (Volatile Escape) - dash explosions (Backblast/Bombs Away) automatically apply
    // Instability regardless of enemy tier. Begin-only, deliberately not paired with End - same
    // reasoning as BirthdayCakeSkillAction: this configures what the ascension enables, not a
    // temporary buff, so re-granting fresh (idempotent) every activation and never revoking it is
    // simplest. Requires MarkExplosiveDeath to already exist, which it always does once Pixie's own
    // Chain Reaction passive has been applied at spawn.
    public unsafe partial class VolatileEscapeSkillAction : SkillActionData
    {
        public VolatileEscapeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(filter.Entity, out var mark) == true)
            {
                mark->VolatileEscapeEnabled = true;
            }
        }
    }
}
