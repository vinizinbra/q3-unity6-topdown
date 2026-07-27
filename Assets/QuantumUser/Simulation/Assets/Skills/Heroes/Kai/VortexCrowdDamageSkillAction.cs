namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the vortex's damage (constant AreaDamage pulses, mini
    // explosions, and the final blast on expiry) scales up with how many enemies are currently caught
    // in its pull - see VortexCrowdDamageUpgrade and VortexSystem.ResolveCrowdMultiplier. A single
    // enemy caught deals baseline (unscaled) damage; each additional enemy up to MaxCount adds
    // PerEnemyBonus more.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexCrowdDamageSkillAction : SkillActionData
    {
        public FP PerEnemyBonus = FP._0_50;
        public int MaxCount = 5;

        public VortexCrowdDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexCrowdDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->PerEnemyBonus = PerEnemyBonus;
            upgrade->MaxCount = MaxCount;
        }
    }
}
