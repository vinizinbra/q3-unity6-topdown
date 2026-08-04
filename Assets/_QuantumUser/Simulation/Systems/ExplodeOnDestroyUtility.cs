namespace Quantum
{
    using Photon.Deterministic;

    // Shared detonation logic for ExplodeOnDestroy (see that component's own comment) - called from
    // both of its trigger points, DestroyAfterTimeSystem (timed expiry) and DamageUtility.ApplyDamage
    // (killed by damage), so the two triggers can't drift into two slightly different explosions.
    // By default (ExplodeOnDestroy.TriggersSpawnUpgrades == false) this mirrors AreaHitData.
    // Detonate's own explosion call (HitEffectUtility.ApplyInRadius + AreaDetonated) without needing
    // a Projectile*, which nothing carrying ExplodeOnDestroy ever has, and never cascades into
    // anything else (no Mini Ordnance signal, no Fireworks/ClusterBomb spawn) - see
    // ExplodeOnDestroy.SpawnDepth's own comment. TriggersSpawnUpgrades == true opts a specific
    // entity (e.g. a planted bomb continuing a real player throw - see ProjectileSystem.TryPlant)
    // into calling AreaHitData.Detonate's full, unabridged version instead, for parity with what it
    // would have done had it detonated immediately instead of via a fuse.
    public static unsafe class ExplodeOnDestroyUtility
    {
        // No-ops (does nothing) if entity has no ExplodeOnDestroy - always safe to call unconditionally
        // from a generic destroy path, same "optional, not a Filter requirement" idiom AreaDamageSystem's
        // own ResolveOwner already uses.
        public static void TryDetonate(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ExplodeOnDestroy>(entity, out var explode) == false)
                return;

            if (explode->Explosion.IsValid == false)
            {
                Log.Error($"[Effect] {entity}'s ExplodeOnDestroy expired with no Explosion asset assigned - nothing detonated");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == false)
            {
                Log.Error($"[Effect] {entity}'s ExplodeOnDestroy has no Transform3D - nothing detonated");
                return;
            }

            EntityRef owner = EntityRef.None;
            DamageSource source = DamageSource.None;
            ElementType element = ElementType.Neutral;

            if (f.Unsafe.TryGetPointer<AreaOwner>(entity, out var areaOwner) == true)
            {
                owner = areaOwner->Owner;
                source = areaOwner->Source;
                element = areaOwner->Element;
            }

            AreaHitData explosion = f.FindAsset(explode->Explosion);

            // Opt-in only (see ExplodeOnDestroy.TriggersSpawnUpgrades) - everything that doesn't set
            // it (Mini Bomb, DashBomb) keeps this exact lighter call, unchanged, so it still can
            // never cascade into another Mini Ordnance/Fireworks/ClusterBomb spawn.
            if (explode->TriggersSpawnUpgrades == true)
            {
                explosion.Detonate(f, owner, source, element, explode->Damage, explode->SpawnDepth, transform->Position);
                Log.Debug($"[Effect] {entity} detonated on destroy at {transform->Position} (owner {owner}, depth {explode->SpawnDepth}, cascade-eligible)");
                return;
            }

            FP radius = ResolveBlastRadius(f, explosion.BlastRadius, owner);

            HitEffectUtility.ApplyInRadius(f, explosion.Effects, transform->Position, radius, owner,
                explode->Damage, source, element, explosion.TargetMask, isExplosion: true);

            f.Events.AreaDetonated(owner, transform->Position, explode->Explosion, radius);

            Log.Debug($"[Effect] {entity} detonated on destroy at {transform->Position} (owner {owner}, depth {explode->SpawnDepth})");
        }

        // Same BlastRadiusUpgrade/Bigger Boom scaling AreaHitData.Detonate applies to a directly-
        // thrown bomb - already gated generically by MarkExplosiveDeath's own presence check (not an
        // "is this Pixie" check), so calling it unconditionally here is exactly as safe as
        // AreaHitData.Detonate already doing so for any Projectile.
        private static FP ResolveBlastRadius(Frame f, FP baseRadius, EntityRef owner)
        {
            FP bonus = f.Unsafe.TryGetPointer<BlastRadiusUpgrade>(owner, out var upgrade) == true ? upgrade->RadiusBonus : FP._0;
            return (baseRadius + bonus) * DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, owner);
        }
    }
}
