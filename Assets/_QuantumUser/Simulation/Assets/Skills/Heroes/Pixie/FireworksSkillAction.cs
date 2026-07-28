namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Upgrade - while equipped, the bomb's detonation also launches Count homing
    // projectiles at random enemies caught within the bomb's own blast area (see
    // AreaHitData.TrySpawnFireworks).
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Detonate() actually reading it - Begin/End brackets the throw itself, which
    // ends the tick after the bomb detonates, often before the detonation logic gets a chance to
    // read this tag. Re-granting fresh (idempotent) every activation and never removing it
    // sidesteps that race entirely.
    public unsafe partial class FireworksSkillAction : SkillActionData
    {
        public int Count = 3;
        public FP Damage = 10;
        public FP LaunchForce = 10;

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> Projectile;

        public FireworksSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        // {0} = Count, {1} = Damage - e.g. "...launches {0} homing firework projectiles dealing {1}
        // damage each..."
        protected override object[] DescriptionArgs => new object[] { Count, Damage };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            f.AddOrGet<FireworksUpgrade>(filter.Entity, out var fireworks);
            fireworks->Count = (byte)Count;
            fireworks->Damage = Damage;
            fireworks->LaunchForce = LaunchForce;
            fireworks->Projectile = Projectile;
        }
    }
}
