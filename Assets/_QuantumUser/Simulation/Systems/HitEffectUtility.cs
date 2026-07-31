namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Runs HitEffectData lists - shared by projectile impacts (ProjectileHitData) and spawned areas
    // (AreaDamageSystem) so the overlap-and-apply loop lives in one place. Two shapes of effect
    // collection rather than one because an asset holds a List while a component holds a FixedArray;
    // both run the same context through the same effects.
    public static unsafe class HitEffectUtility
    {
        // multiTarget defaults false (a guaranteed single-connect hit, e.g. ProjectileHitData's own
        // impact) - see HitEffectApplied's own comment. The three overlap-query methods below
        // (ApplyInRadius/ApplyInShape/ApplyInCollider) always pass true, since an overlap query can
        // always catch more than one entity even when this particular call happens to connect with
        // just one.
        public static void ApplyToTarget(Frame f, List<AssetRef<HitEffectData>> effects, ref HitEffectContext context, bool multiTarget = false)
        {
            StatusEffectUtility.TryApplyElementalStatus(f, context.Target, context.Owner, context.Source, context.Element, context.Damage);

            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].IsValid == false)
                    continue;

                f.FindAsset(effects[i]).Apply(f, ref context);
            }

            f.Events.HitEffectApplied(context.Owner, context.Target, context.Position, multiTarget);
        }

        public static void ApplyToTarget(Frame f, FixedArray<AssetRef<HitEffectData>> effects, ref HitEffectContext context, bool multiTarget = false)
        {
            StatusEffectUtility.TryApplyElementalStatus(f, context.Target, context.Owner, context.Source, context.Element, context.Damage);

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].IsValid == false)
                    continue;

                f.FindAsset(effects[i]).Apply(f, ref context);
            }

            f.Events.HitEffectApplied(context.Owner, context.Target, context.Position, multiTarget);
        }

        // For a blast with no entity behind it - the projectile that carried it is already gone, so
        // the radius has to come from data. targetMask defaults to Both to preserve every existing
        // caller's behavior - AreaHitData is the one that actually opts into Enemies so a player-
        // thrown bomb doesn't catch allies in its own blast.
        // isExplosion flags this radius hit as a genuine area/explosive blast (see
        // HitEffectContext.IsExplosion's own comment) - defaults false, so every existing caller is
        // unaffected; AreaHitData.Detonate (Pixie's own bomb) is the one that passes true.
        public static void ApplyInRadius(Frame f, List<AssetRef<HitEffectData>> effects, FPVector3 center,
            FP radius, EntityRef owner, FP damage, DamageSource source, ElementType element = ElementType.Neutral,
            DamageTargetMask targetMask = DamageTargetMask.Both, bool isExplosion = false)
        {
            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            Log.Debug($"[Effect] {owner}'s blast at {center} radius {radius} caught {hits.Count} shapes");

            for (int i = 0; i < hits.Count; i++)
            {
                if (MatchesTargetMask(f, hits[i].Entity, targetMask) == false)
                    continue;

                if (TryBuildContext(f, hits[i].Entity, center, owner, damage, source, element, out var context, isExplosion: isExplosion) == false)
                    continue;

                ApplyToTarget(f, effects, ref context, multiTarget: true);
            }
        }

        // For a hit that sweeps a volume instead of radiating from a point. The caller supplies one
        // push direction for everyone caught, since a sweep shoves along its own axis - radial from
        // the center of a swept path would leave whoever it ran straight through with a near-zero
        // vector, and push the ones at its start backwards.
        public static void ApplyInShape(Frame f, List<AssetRef<HitEffectData>> effects, FPVector3 center,
            FPQuaternion rotation, Shape3D shape, EntityRef owner, FP damage,
            DamageSource source, FPVector3 pushDirection, ElementType element = ElementType.Neutral)
        {
            var hits = f.Physics3D.OverlapShape(center, rotation, shape, -1, QueryOptions.HitAll);

            Log.Debug($"[Effect] {owner}'s sweep at {center} caught {hits.Count} shapes");

            for (int i = 0; i < hits.Count; i++)
            {
                if (TryBuildContext(f, hits[i].Entity, center, owner, damage, source, element, out var context,
                        pushDirection) == false)
                    continue;

                ApplyToTarget(f, effects, ref context, multiTarget: true);
            }
        }

        // For an area that exists as an entity - its own collider is the shape, so what hurts is
        // exactly what was authored and rendered, with no second radius to drift out of sync.
        // pushDirection overrides the default radial-from-center push - see ApplyInShape; a
        // stationary area has no swept direction to fall back on, so unlike ApplyInShape this stays
        // optional and defaults to radial.
        public static void ApplyInCollider(Frame f, FixedArray<AssetRef<HitEffectData>> effects, Transform3D* transform,
            PhysicsCollider3D* collider, EntityRef owner, FP damage, DamageSource source,
            FPVector3? pushDirection = null, ElementType element = ElementType.Neutral,
            DamageTargetMask targetMask = DamageTargetMask.Both)
        {
            // Takes the transform and shape by value, and applies the shape's own local offset
            // relative to it - so a collider authored off-center overlaps where it actually sits.
            var hits = f.Physics3D.OverlapShape(*transform, collider->Shape, -1, QueryOptions.HitAll);

            Log.Debug($"[Effect] {owner}'s area at {transform->Position} caught {hits.Count} shapes " +
                      $"(pushDirection {(pushDirection.HasValue ? pushDirection.Value.ToString() : "radial")})");

            for (int i = 0; i < hits.Count; i++)
            {
                if (MatchesTargetMask(f, hits[i].Entity, targetMask) == false)
                    continue;

                if (TryBuildContext(f, hits[i].Entity, transform->Position, owner, damage, source, element, out var context,
                        pushDirection) == false)
                    continue;

                ApplyToTarget(f, effects, ref context, multiTarget: true);
            }
        }

        // Both (the default) matches this codebase's behavior before this concept existed - no
        // filtering at all. Players/Enemies check component presence rather than e.g. a side/team
        // field, since that's already exactly how the rest of the codebase tells the two apart
        // (WeaponSystem/CharacterSystem key off PlayerLink, EnemySystem/DamageUtility off Enemy).
        private static bool MatchesTargetMask(Frame f, EntityRef target, DamageTargetMask mask)
        {
            switch (mask)
            {
                case DamageTargetMask.Players: return f.Has<PlayerLink>(target);
                case DamageTargetMask.Enemies: return f.Has<Enemy>(target);
                default: return true;
            }
        }

        // For a blast that just needs to hurt whoever's caught, with no HitEffectData/status-proc
        // processing on top (e.g. DamageUtility's ExplodeOnDeath chain) - skips the whole
        // context/Effects indirection above and calls DamageUtility.ApplyDamage directly. See
        // ApplyInRadius for the effects-driven version. targetMask defaults to Both to preserve
        // every existing caller's behavior.
        // isChainedExplosion/isDashExplosion both feed DamageUtility.ApplyDamage's own
        // TryMarkExplodeOnDeath call (Pixie's Chain Reaction/Volatile Escape ascensions) - see that
        // method's own comment. Both default false, so every existing caller is unaffected.
        public static void ApplyDamageInRadius(Frame f, FPVector3 center, FP radius, EntityRef owner, FP damage,
            DamageSource source, DamageTargetMask targetMask = DamageTargetMask.Both,
            bool isChainedExplosion = false, bool isDashExplosion = false, bool isExplosion = false)
        {
            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            Log.Debug($"[Effect] {owner}'s blast at {center} radius {radius} caught {hits.Count} shapes");

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (target == EntityRef.None || target == owner)
                    continue;

                if (MatchesTargetMask(f, target, targetMask) == false)
                    continue;

                DamageUtility.ApplyDamage(f, target, damage, owner, source,
                    isChainedExplosion: isChainedExplosion, isDashExplosion: isDashExplosion, isExplosion: isExplosion);
            }
        }

        // A radial knockback-only burst around a point - no damage, no Effects/HitEffectContext
        // indirection, just DamageUtility.ApplyKnockback per target caught in radius (pushed
        // straight away from center, no vertical lift) plus the ShockwaveReleased view hook. The
        // generic entry point for any "empty magazine releases a shockwave"-style effect (currently
        // only the Empty Chamber weapon perk, see WeaponSystem.ApplyMagazineEmptiedPerks) - callers
        // don't need their own sphere-overlap-plus-knockback-loop, and share one VFX hookup
        // (EffectsManager.OnShockwaveReleased) instead of each wiring up their own. Fires the event
        // unconditionally, even if nothing was actually caught, so it still reads visually against
        // an empty room - same convention AreaDetonated/ExplodeOnDeathDetonated already follow.
        // effect is invalid/default for every caller except Zara's Remix ascension (see
        // ResonanceUtility.ResolveRemixEffect) - passed straight through to the event so the View can
        // tint this specific shockwave's particle by which HitEffectData was randomly chosen, without
        // this utility needing to know or care about color itself.
        public static void ApplyShockwave(Frame f, FPVector3 center, FP radius, EntityRef owner, FP knockbackForce,
            DamageTargetMask targetMask = DamageTargetMask.Enemies, AssetRef<HitEffectData> effect = default)
        {
            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (target == owner || MatchesTargetMask(f, target, targetMask) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                DamageUtility.ApplyKnockback(f, target, targetTransform->Position - center, knockbackForce, FP._0, owner);
            }

            f.Events.ShockwaveReleased(owner, center, radius, effect);
        }

        // A radial damage-only blast (no knockback) - ApplyDamageInRadius plus the
        // WeaponExplosionReleased view hook, the generic entry point for any weapon-perk explosion
        // that has no dedicated VFX of its own (currently Cataclysm Round and Explosive Sequence,
        // see DirectHitData.ApplyTerminalWeaponPerks/WeaponSystem.ApplyHitscanWeaponPerks) - callers
        // share one fallback prefab (EffectsManager.OnWeaponExplosionReleased always plays
        // defaultAreaBlastEffect) instead of each needing their own. Fires the event unconditionally,
        // even if nothing was actually caught, so it still reads visually against an empty room -
        // same convention ApplyShockwave/AreaDetonated already follow.
        // isDashExplosion feeds DamageUtility.ApplyDamage's own TryMarkExplodeOnDeath call (Pixie's
        // Volatile Escape ascension) - see that method's own comment. Defaults false, so every
        // existing caller is unaffected; Pixie's own Backblast dash action passes true.
        public static void ApplyExplosion(Frame f, FPVector3 center, FP radius, EntityRef owner, FP damage,
            DamageSource source, DamageTargetMask targetMask = DamageTargetMask.Enemies, bool isDashExplosion = false)
        {
            // Hardcoded true, not a caller param - anything calling ApplyExplosion at all (weapon-perk
            // explosions, Pixie's own Backblast) is definitionally a genuine explosion.
            ApplyDamageInRadius(f, center, radius, owner, damage, source, targetMask, isDashExplosion: isDashExplosion, isExplosion: true);
            f.Events.WeaponExplosionReleased(owner, center, radius);
        }

        // pushDirection overrides the default radial-from-center push - see ApplyInShape.
        //
        // Deliberately does NOT skip target == owner - unlike ApplyDamageInRadius (which always
        // means a blast, never something an owner should heal from), this feeds every effect type
        // alike, including HealEffectData, which very much needs to be able to reach its own owner
        // (e.g. Zara standing in her own speaker's pulse). Effects that shouldn't hit their owner
        // (DamageEffectData, BurnEffectData, KnockbackEffectData) check context.Target != context.Owner
        // themselves instead.
        private static bool TryBuildContext(Frame f, EntityRef target, FPVector3 center, EntityRef owner,
            FP damage, DamageSource source, ElementType element, out HitEffectContext context,
            FPVector3? pushDirection = null, bool isExplosion = false)
        {
            context = default;

            if (target == EntityRef.None)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return false;

            context = new HitEffectContext
            {
                Owner = owner,
                Target = target,
                Position = targetTransform->Position,
                PushDirection = pushDirection ?? targetTransform->Position - center,
                Damage = damage,
                Source = source,
                Element = element,
                IsExplosion = isExplosion
            };

            return true;
        }
    }
}
