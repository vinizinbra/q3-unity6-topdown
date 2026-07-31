namespace Quantum
{
    using Photon.Deterministic;

    // Dash Ascension (Reflect) - while dashing, any enemy-owned Projectile within Radius of Kai's
    // current position is sent back the way it came (reversed Velocity) and re-owned by him, so it
    // now damages enemies instead of players - same "flip Owner so damage resolves off the new
    // owner's stats" idiom DamageUtility.ApplyDamage already relies on everywhere. Runs every tick
    // of the dash (OnGoing) rather than a single swept-box test at Begin/End - a projectile keeps
    // moving throughout the dash, so a per-tick proximity check catches one that only entered range
    // mid-dash, which a before/after sweep would miss.
    //
    // Excluded against Elite/Boss-owned projectiles (reflecting a boss's own attack back at it would
    // trivialize the fight) - same EnemyTier gate idiom used elsewhere in this roster (Pixie's Heavy
    // Payload, Kai's own Void Pressure).
    public unsafe partial class ReflectProjectilesSkillAction : SkillActionData
    {
        public FP Radius = 3;

        public ReflectProjectilesSkillAction()
        {
            Phase = SkillActionPhase.OnGoing;
        }

        protected override object[] DescriptionArgs => new object[] { Radius };

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            FPVector3 position = filter.Transform3D->Position;
            var projectiles = f.Filter<Projectile, Transform3D>();

            while (projectiles.Next(out EntityRef projectileEntity, out Projectile projectile, out Transform3D projectileTransform))
            {
                // Already-reflected projectiles are no longer Enemy-owned (see below), so this
                // naturally skips re-reflecting the same shot on a later tick of the same dash.
                if (f.Has<Enemy>(projectile.Owner) == false)
                    continue;

                if (IsExcludedTier(f, projectile.Owner) == true)
                    continue;

                if ((projectileTransform.Position - position).SqrMagnitude > Radius * Radius)
                    continue;

                if (f.Unsafe.TryGetPointer<Projectile>(projectileEntity, out var live) == false)
                    continue;

                live->Velocity = -live->Velocity;
                live->Owner = filter.Entity;
                live->Source = DamageSource.Skill;

                f.Events.ProjectileReflected(filter.Entity, projectileTransform.Position);

                Log.Debug($"[Skill] {filter.Entity} reflected {projectileEntity} back the way it came");
            }
        }

        private static bool IsExcludedTier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(owner, out var enemy) == false)
                return false;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return data.Tier >= EnemyTier.Elite;
        }
    }
}
