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
        public static void ApplyToTarget(Frame f, List<AssetRef<HitEffectData>> effects, ref HitEffectContext context)
        {
            StatusEffectUtility.TryApplyElementalStatus(f, context.Target, context.Owner, context.Source, context.Element, context.Damage);

            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].IsValid == false)
                    continue;

                f.FindAsset(effects[i]).Apply(f, ref context);
            }

            f.Events.HitEffectApplied(context.Owner, context.Target, context.Position);
        }

        public static void ApplyToTarget(Frame f, FixedArray<AssetRef<HitEffectData>> effects, ref HitEffectContext context)
        {
            StatusEffectUtility.TryApplyElementalStatus(f, context.Target, context.Owner, context.Source, context.Element, context.Damage);

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].IsValid == false)
                    continue;

                f.FindAsset(effects[i]).Apply(f, ref context);
            }

            f.Events.HitEffectApplied(context.Owner, context.Target, context.Position);
        }

        // For a blast with no entity behind it - the projectile that carried it is already gone, so
        // the radius has to come from data. targetMask defaults to Both to preserve every existing
        // caller's behavior - AreaHitData is the one that actually opts into Enemies so a player-
        // thrown bomb doesn't catch allies in its own blast.
        public static void ApplyInRadius(Frame f, List<AssetRef<HitEffectData>> effects, FPVector3 center,
            FP radius, EntityRef owner, FP damage, DamageSource source, ElementType element = ElementType.Neutral,
            DamageTargetMask targetMask = DamageTargetMask.Both)
        {
            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            Log.Debug($"[Effect] {owner}'s blast at {center} radius {radius} caught {hits.Count} shapes");

            for (int i = 0; i < hits.Count; i++)
            {
                if (MatchesTargetMask(f, hits[i].Entity, targetMask) == false)
                    continue;

                if (TryBuildContext(f, hits[i].Entity, center, owner, damage, source, element, out var context) == false)
                    continue;

                ApplyToTarget(f, effects, ref context);
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

                ApplyToTarget(f, effects, ref context);
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

                ApplyToTarget(f, effects, ref context);
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
        public static void ApplyDamageInRadius(Frame f, FPVector3 center, FP radius, EntityRef owner, FP damage,
            DamageSource source, DamageTargetMask targetMask = DamageTargetMask.Both)
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

                DamageUtility.ApplyDamage(f, target, damage, owner, source);
            }
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
            FPVector3? pushDirection = null)
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
                Element = element
            };

            return true;
        }
    }
}
