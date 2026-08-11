namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared detonation logic for ExplodeOnDestroy (see that component's own comment) - called from
    // both of its trigger points, DestroyAfterTimeSystem (timed expiry) and DamageUtility.ApplyDamage
    // (killed by damage), so the two triggers can't drift into two slightly different explosions.
    // By default (ExplodeOnDestroy.TriggersSpawnUpgrades == false) this mirrors AreaHitData.
    // Detonate's own explosion call (HitEffectUtility.ApplyInRadius + AreaDetonated) without needing
    // a Projectile*, which nothing carrying ExplodeOnDestroy ever has, and never cascades into
    // anything else (no Pocket Bombs signal, no Fireworks/ClusterBomb spawn) - see
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
            FP damage = ApplyBirthdayCakeBonus(f, entity, owner, explode->Damage);
            FP radiusMultiplier = ResolveBirthdayCakeRadiusMultiplier(f, entity, owner);

            // Opt-in only (see ExplodeOnDestroy.TriggersSpawnUpgrades) - everything that doesn't set
            // it (Mini Bomb, DashBomb) keeps this exact lighter call, unchanged, so it still can
            // never cascade into another Pocket Bombs/ClusterBomb spawn.
            if (explode->TriggersSpawnUpgrades == true)
            {
                FP blastRadius = explosion.Detonate(f, owner, source, element, damage, explode->SpawnDepth, transform->Position, radiusMultiplier);

                // Backblast rank 3 (Pixie ascension) - granted onto this specific bomb entity, not
                // the owner, so it never leaks onto an unrelated explosion (Bunny Bomb, a Pocket
                // Bombs drop) the same owner causes. See ForceMarkOnDetonate.qtn.
                if (f.Has<ForceMarkOnDetonate>(entity) == true)
                {
                    ForceMarkEnemiesInRadius(f, transform->Position, blastRadius);
                }

                Log.Debug($"[Effect] {entity} detonated on destroy at {transform->Position} (owner {owner}, depth {explode->SpawnDepth}, cascade-eligible)");
                return;
            }

            FP radius = ResolveBlastRadius(f, explosion.BlastRadius, owner) * radiusMultiplier;

            HitEffectUtility.ApplyInRadius(f, explosion.Effects, transform->Position, radius, owner,
                damage, source, element, explosion.TargetMask, isExplosion: true);

            f.Events.AreaDetonated(owner, transform->Position, explode->Explosion, radius);

            Log.Debug($"[Effect] {entity} detonated on destroy at {transform->Position} (owner {owner}, depth {explode->SpawnDepth})");
        }

        // Same Unstable Mixture/Skill Area scaling AreaHitData.Detonate applies to a directly-thrown
        // bomb - already gated generically by MarkExplosiveDeath's own presence check (not an "is this
        // Pixie" check), so calling it unconditionally here is exactly as safe as AreaHitData.Detonate
        // already doing so for any Projectile.
        private static FP ResolveBlastRadius(Frame f, FP baseRadius, EntityRef owner)
        {
            return baseRadius * DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, owner)
                * StatUtility.GetAreaMultiplier(f, owner);
        }

        // Birthday Cake (Pixie ascension) - both gated on the detonating entity itself currently being
        // a Decoy, which is only ever true if it was actively taunting (see ProjectileSystem.TryPlant,
        // the only thing that ever adds Decoy to a bomb rather than an enemy trap) - so both only ever
        // fire for Birthday Cake's own landed Bunny Bomb, never Pocket Bombs's Mini Bomb or a dropped
        // DashBomb. Rank 2's TauntRadiusMultiplier scales the whole blast radius (the generic decoy-
        // pull mechanic has no radius knob of its own to scale, see BirthdayCakeUpgrade.qtn); rank 3's
        // HasBonusDamage scales the whole detonation's damage rather than a per-target proximity check
        // - the taunt radius is always >= the blast's own damage radius, so every enemy this blast
        // actually damages was already within taunt range.
        private static FP ApplyBirthdayCakeBonus(Frame f, EntityRef entity, EntityRef owner, FP damage)
        {
            if (f.Has<Decoy>(entity) == false)
                return damage;

            if (f.Unsafe.TryGetPointer<BirthdayCakeUpgrade>(owner, out var birthdayCake) == false || birthdayCake->HasBonusDamage == false)
                return damage;

            return damage * (FP._1 + birthdayCake->BonusDamageMultiplier);
        }

        private static FP ResolveBirthdayCakeRadiusMultiplier(Frame f, EntityRef entity, EntityRef owner)
        {
            if (f.Has<Decoy>(entity) == false)
                return FP._1;

            return f.Unsafe.TryGetPointer<BirthdayCakeUpgrade>(owner, out var birthdayCake) == true
                ? birthdayCake->TauntRadiusMultiplier
                : FP._1;
        }

        // ForceMarkOnDetonate (see that component's own comment) - unconditionally grants/refreshes
        // ExplodeOnDeath on every enemy caught, bypassing MarkExplosiveDeath's own tier-gate/chance
        // roll entirely (this doesn't go through TryMarkExplodeOnDeath at all). Uses
        // ExplodeOnDeathConfig.Duration, the same shared balance knob a normal mark uses, so a
        // guaranteed mark still "cures" after the usual duration rather than lasting forever.
        private static void ForceMarkEnemiesInRadius(Frame f, FPVector3 center, FP radius)
        {
            if (radius <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);
            ExplodeOnDeathConfig config = f.FindAsset(f.RuntimeConfig.ExplodeOnDeathConfig);

            for (int i = 0; i < hits.Count; i++)
            {
                if (f.Has<Enemy>(hits[i].Entity) == false)
                    continue;

                f.AddOrGet<ExplodeOnDeath>(hits[i].Entity, out var explode);
                explode->Remaining = config.Duration;
            }
        }
    }
}
