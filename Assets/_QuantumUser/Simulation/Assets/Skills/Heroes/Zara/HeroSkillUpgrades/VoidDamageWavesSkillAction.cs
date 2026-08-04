namespace Quantum
{
    // Hero Skill Upgrade - while equipped, the speaker spawns with its damage pulse also applying
    // RiftMarkEffect (typically a RiftMarkEffectData asset) - see
    // SpawnAlternatingAreaEffectData.ApplyVoidUpgrade, baked into each speaker once at spawn.
    // Migrated from PoisonDamageWavesSkillAction once Poison was removed, then from granting Void to
    // granting a Rift Mark stack once Void was promoted to a real damage element (see
    // docs/elemental-reactions.md) - this class keeps its historical "VoidDamageWaves" name (same
    // precedent as that earlier migration); only the effect it grants changed. It marks enemies with
    // a Rift Mark, priming them for whichever element (this Zara's own weapon, or a teammate's)
    // lands next - a team-enabling skill rather than a direct damage-over-time source.
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
        [ExpandableAsset] public AssetRef<HitEffectData> RiftMarkEffect;

        public VoidDamageWavesSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VoidDamageWavesUpgrade>(filter.Entity, out var upgrade);
            upgrade->RiftMarkEffect = RiftMarkEffect;

            Log.Debug($"[Skill] {filter.Entity} granted VoidDamageWavesUpgrade (RiftMarkEffect valid: {RiftMarkEffect.IsValid})");
        }
    }
}
