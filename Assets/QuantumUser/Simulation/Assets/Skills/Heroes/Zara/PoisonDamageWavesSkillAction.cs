namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the speaker spawns with its damage pulse also applying
    // PoisonEffect (typically a PoisonEffectData asset) - see
    // SpawnAlternatingAreaEffectData.ApplyPoisonUpgrade, baked into each speaker once at spawn.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving (contrast Max's
    // Burn-on-hit, which only matters while Berserk itself is Active). Revoking it on End raced
    // against the moment the skill's own throw actually spawns something - Begin/End brackets the
    // throw itself, which ends the tick after the projectile lands, often before whatever reads this
    // tag gets a chance to. Re-granting fresh (idempotent) every activation and never removing it
    // sidesteps that race entirely instead of trying to win it.
    public unsafe partial class PoisonDamageWavesSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<HitEffectData> PoisonEffect;

        public PoisonDamageWavesSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<PoisonDamageWavesUpgrade>(filter.Entity, out var upgrade);
            upgrade->PoisonEffect = PoisonEffect;

            Log.Debug($"[Skill] {filter.Entity} granted PoisonDamageWavesUpgrade (PoisonEffect valid: {PoisonEffect.IsValid})");
        }
    }
}
