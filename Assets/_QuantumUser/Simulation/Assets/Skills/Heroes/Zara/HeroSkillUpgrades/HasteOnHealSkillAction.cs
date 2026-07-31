namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the speaker spawns with its heal pulse also applying
    // HasteEffect (typically a HasteEffectData asset) to whoever it heals - see
    // SpawnAlternatingAreaEffectData.ApplyHasteUpgrade, baked into each speaker once at spawn.
    //
    // Begin-only, deliberately not paired with End - same reasoning as
    // Heroes/Zara/VoidDamageWavesSkillAction: this configures what the skill produces, and
    // revoking on End would race against the moment the throw actually spawns the speaker.
    // Re-granting fresh (idempotent) every activation and never removing it sidesteps that race.
    public unsafe partial class HasteOnHealSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<HitEffectData> HasteEffect;

        public HasteOnHealSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<HasteOnHealUpgrade>(filter.Entity, out var upgrade);
            upgrade->HasteEffect = HasteEffect;

            Log.Debug($"[Skill] {filter.Entity} granted HasteOnHealUpgrade (HasteEffect valid: {HasteEffect.IsValid})");
        }
    }
}
