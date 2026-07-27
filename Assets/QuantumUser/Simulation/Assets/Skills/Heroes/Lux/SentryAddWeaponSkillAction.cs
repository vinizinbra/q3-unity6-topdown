namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - arms one of the sentry's 4 weapon slots (see SentryWeaponUpgrade); a
    // sentry with none of the 4 slots filled has no barrels/weapons at all
    // (SpawnSentrySkillAction.ApplyWeaponUpgrade only spawns a SentryBarrel per slot that has a
    // valid WeaponData). Each of the 4 weapon-upgrade asset instances targets a different SlotIndex
    // (0-3), so up to 4 independent picks arm up to 4 simultaneously-firing weapons. WeaponOffset is
    // the local-space muzzle position (X=right, Y=up, Z=forward) for that slot's barrel, baked into
    // its own Transform3D.Position at spawn - see SpawnSentrySkillAction.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // the skill does" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SentryAddWeaponSkillAction : SkillActionData
    {
        public byte SlotIndex;
        public AssetRef<WeaponDataAsset> WeaponData;
        public FPVector3 WeaponOffset;

        public SentryAddWeaponSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<SentryWeaponUpgrade>(filter.Entity, out var upgrade);
            upgrade->WeaponData[SlotIndex] = WeaponData;
            upgrade->WeaponOffset[SlotIndex] = WeaponOffset;
            upgrade->Source[SlotIndex] = this;
        }
    }
}
