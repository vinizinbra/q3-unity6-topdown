namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, whatever the skill spawns has its own collider ScaleBonus
    // bigger than what's authored on its prototype - see SpawnedEntitySpawner.ApplyRadiusUpgrade.
    // Generic rather than hero-specific: SpawnRadiusUpgrade already scales whatever collider any
    // spawned entity has, so one class covers every hero (Zara's speaker, Kai's vortex, and anything
    // future) - create a separate .asset instance per hero and name it accordingly rather than a
    // separate subclass.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against the moment the throw actually spawns the entity - Begin/End brackets the
    // throw itself, which ends the tick after the projectile lands, often before the spawn logic
    // gets a chance to read this tag. Re-granting fresh (idempotent) every activation and never
    // removing it sidesteps that race entirely.
    public unsafe partial class SpawnRadiusUpSkillAction : SkillActionData
    {
        public FP ScaleBonus = FP._0_50;

        public SpawnRadiusUpSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SpawnRadiusUpgrade>(filter.Entity, out var upgrade);
            upgrade->ScaleBonus = ScaleBonus;
        }
    }
}
