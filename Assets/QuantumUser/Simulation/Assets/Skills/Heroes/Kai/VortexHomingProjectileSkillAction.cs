namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the vortex periodically fires a homing projectile at the
    // nearest enemy within (vortex's own pull radius * SearchRadiusMultiplier), every TickInterval
    // seconds, for as long as it's alive - reaches enemies outside the pull itself, not just whatever
    // is already caught. See VortexHomingProjectileUpgrade and VortexSystem.TryHomingProjectile.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class VortexHomingProjectileSkillAction : SkillActionData
    {
        public AssetRef<ProjectileDataAsset> Projectile;
        public FP Damage = 10;
        public FP SearchRadiusMultiplier = FP.FromString("1.2");
        public FP TickInterval = 1;

        public VortexHomingProjectileSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<VortexHomingProjectileUpgrade>(filter.Entity, out var upgrade);
            upgrade->Projectile = Projectile;
            upgrade->Damage = Damage;
            upgrade->SearchRadiusMultiplier = SearchRadiusMultiplier;
            upgrade->TickInterval = TickInterval;
        }
    }
}
