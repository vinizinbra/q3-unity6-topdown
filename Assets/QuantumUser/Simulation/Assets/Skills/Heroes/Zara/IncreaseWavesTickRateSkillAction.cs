namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the speaker spawns pulsing RateBonus faster than what's
    // authored on SpawnAlternatingAreaEffectData.TickInterval - see
    // SpawnAlternatingAreaEffectData.ResolveTickInterval.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: revoking on End would race against the moment the throw
    // actually spawns the speaker. Re-granting fresh (idempotent) every activation and never
    // removing it sidesteps that race.
    public unsafe partial class IncreaseWavesTickRateSkillAction : SkillActionData
    {
        public FP RateBonus = FP._0_50;

        public IncreaseWavesTickRateSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<TickRateUpgrade>(filter.Entity, out var upgrade);
            upgrade->RateBonus = RateBonus;
        }
    }
}
