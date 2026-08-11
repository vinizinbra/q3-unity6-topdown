namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Base for who a projectile's hit resolves onto; each subclass is its own Quantum asset. What
    // then happens to each of those targets is the Effects list, not this - a projectile with no
    // effects hits for nothing.
    public abstract unsafe class ProjectileHitData : AssetObject
    {
        [ExpandableAsset] public List<AssetRef<HitEffectData>> Effects = new();

        // How far above a settled contact point (see ProjectileSystem's justGrounded snap and
        // Settle below) this projectile's Transform rests, along world Up - the raycast hit point
        // itself is the ground surface, so a pivot-at-center model needs this tuned to roughly its
        // own visual half-height or it settles half-embedded. Zero (the default) is right for
        // anything that never settles - destroyed on hit, or piercing through.
        public FP RestOffset;

        // False turns a hit against anything that isn't a player or an enemy - static level
        // geometry (hitEntity == None) or a dynamic prop/door (a real entity, but neither tagged
        // component) - into something that doesn't count as a genuine hit. What "doesn't count"
        // means is up to the subclass (AreaHitData settles and waits out its fuse; pierce-style hit
        // data would just keep flying) - this only decides whether the contact should register at
        // all. True (the default) is right for anything that should just hit whatever it touches.
        public bool DetonateOnLevelGeometry = true;

        // Same idea as DetonateOnLevelGeometry, for a hit against an enemy specifically - false lets
        // it pass without registering as a hit there either. A player hit is unaffected by either
        // flag; there's no toggle for that case yet.
        public bool DetonateOnEnemyHit = true;

        // Seeds whatever per-shot state the hit behavior tracks on the component.
        public virtual void Initialize(Projectile* projectile)
        {
        }

        // hitEntity is EntityRef.None when the projectile struck level geometry. Returns true when
        // the projectile is spent and should be destroyed.
        public abstract bool ApplyHit(Frame f, Projectile* projectile, EntityRef hitEntity, FPVector3 point);

        // Lifetime ran out without connecting.
        public virtual void ApplyExpire(Frame f, Projectile* projectile, FPVector3 position)
        {
        }

        protected void ApplyEffects(Frame f, Projectile* projectile, EntityRef target,
            FPVector3 position, FPVector3 pushDirection)
        {
            HitEffectContext context = new HitEffectContext
            {
                Owner = projectile->Owner,
                Target = target,
                Position = position,
                PushDirection = pushDirection,
                Damage = projectile->Damage,
                Source = projectile->Source,
                Element = projectile->Element,
                PerkElement = projectile->PerkElement,
                PerkElementChance = projectile->PerkElementChance,
                HitIndex = projectile->PelletIndex
            };

            HitEffectUtility.ApplyToTarget(f, Effects, ref context);
        }

        // InstantDetonate (an upgrade - see Heroes/Pixie/HotFuseSkillAction) overrides
        // DetonateOnEnemyHit specifically - a direct enemy hit always counts as a genuine hit once
        // granted, regardless of that flag's authored value. Deliberately does NOT override
        // DetonateOnLevelGeometry/the planting path below - a ground/geometry contact is unaffected,
        // so a planted bomb (see AreaHitData.PlantedFuseTime/ProjectileSystem.TryPlant) still lands
        // and runs its own fuse/taunt behavior (e.g. Birthday Cake) exactly as it would without this
        // upgrade. Only the enemy-hit path is meant to skip straight to detonation.
        protected bool ShouldDetonate(Frame f, Projectile* projectile, EntityRef hitEntity)
        {
            if (hitEntity != EntityRef.None && f.Has<Enemy>(hitEntity) == true)
                return DetonateOnEnemyHit || f.Has<InstantDetonate>(projectile->Owner) == true;

            if (IsCombatant(f, hitEntity) == true) // a player - no toggle requested yet, always counts
                return true;

            return DetonateOnLevelGeometry;
        }

        // Static geometry has no entity at all (None); dynamic geometry does, but is neither an
        // Enemy nor a PlayerLink - same check DamageTargetMask filtering uses to tell a real target
        // from scenery, see HitEffectUtility.MatchesTargetMask.
        protected static bool IsCombatant(Frame f, EntityRef entity)
        {
            return entity != EntityRef.None && (f.Has<Enemy>(entity) == true || f.Has<PlayerLink>(entity) == true);
        }

        // Stops movement dead rather than letting an ignored hit keep drifting - pairs with
        // ThrownProjectileMovementData, which stops re-applying gravity once Grounded is set, and
        // ProjectileSystem's justGrounded snap, which stops it exactly at the contact point instead
        // of overshooting into whatever it landed on.
        protected static void Settle(Projectile* projectile)
        {
            projectile->Velocity = FPVector3.Zero;
            projectile->Grounded = true;
        }
    }
}
