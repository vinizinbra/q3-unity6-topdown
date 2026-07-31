namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the speaker spawns with its damage pulse also applying
    // VoidEffect (typically a VoidEffectData asset) - see
    // SpawnAlternatingAreaEffectData.ApplyVoidUpgrade, baked into each speaker once at spawn.
    // Migrated from PoisonDamageWavesSkillAction once Poison was removed (see
    // docs/elemental-reactions.md) - Void has no damage of its own, so this skill went from a direct
    // damage-over-time source to a team-enabling one: it marks enemies with Void, priming them for
    // whichever element (this Zara's own weapon, or a teammate's) lands next.
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving (contrast Max's
    // Burn-on-hit, which only matters while Berserk itself is Active). Revoking it on End raced
    // against the moment the skill's own throw actually spawns something - Begin/End brackets the
    // throw itself, which ends the tick after the projectile lands, often before whatever reads this
    // tag gets a chance to. Re-granting fresh (idempotent) every activation and never removing it
    // sidesteps that race entirely instead of trying to win it.
    public unsafe partial class VoidDamageWavesSkillAction : SkillActionData
    {
        [ExpandableAsset] public AssetRef<HitEffectData> VoidEffect;

        public VoidDamageWavesSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VoidDamageWavesUpgrade>(filter.Entity, out var upgrade);
            upgrade->VoidEffect = VoidEffect;

            Log.Debug($"[Skill] {filter.Entity} granted VoidDamageWavesUpgrade (VoidEffect valid: {VoidEffect.IsValid})");
        }
    }
}
