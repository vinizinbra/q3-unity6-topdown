namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the speaker spawns with its damage pulse also applying
    // KnockbackEffect (typically a KnockbackEffectData asset) - see
    // SpawnAlternatingAreaEffectData.ApplyKnockbackUpgrade, baked into each speaker once at spawn.
    //
    // Begin-only, deliberately not paired with End - same reasoning as
    // Heroes/Zara/PoisonDamageWavesSkillAction: this configures what the skill produces, and
    // revoking on End would race against the moment the throw actually spawns the speaker.
    public unsafe partial class KnockbackOnDamageSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<HitEffectData> KnockbackEffect;

        public KnockbackOnDamageSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<KnockbackOnDamageUpgrade>(filter.Entity, out var upgrade);
            upgrade->KnockbackEffect = KnockbackEffect;

            Log.Debug($"[Skill] {filter.Entity} granted KnockbackOnDamageUpgrade (KnockbackEffect valid: {KnockbackEffect.IsValid})");
        }
    }
}
