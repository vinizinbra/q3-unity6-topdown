namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared by skills (SpawnEntitySkillAction) and projectile impacts (SpawnEntityEffectData) so
    // both drop things into the world the same way: a prefab, an owner, and how long it lives. What
    // the spawned thing then does to people is the prototype's own business - a prefab carrying an
    // AreaDamage hurts whoever stands in it, one carrying ExplodeOnDestroy detonates once instead
    // (e.g. Pixie's Dash Ascension "Leave Explosive Bomb" - a stationary, optionally damageable
    // decoy-trap bomb, see ExplodeOnDestroy.qtn), one with neither just sits there - but every spawn
    // gets its ownership (AreaOwner) and lifetime (DestroyAfterTime) stamped identically regardless
    // of which.
    public static unsafe class SpawnedEntitySpawner
    {
        public static EntityRef Spawn(Frame f, EntityRef owner, AssetRef<EntityPrototype> prototype,
            FP duration, FPVector3 position, DamageSource source = DamageSource.None,
            ElementType element = ElementType.Neutral, FP? damageOverride = null,
            DamageTargetMask? targetMaskOverride = null)
        {
            if (prototype.IsValid == false)
                return EntityRef.None;

            EntityRef entity = f.Create(prototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == true)
            {
                transform->Position = position;
                GroundOffsetUtility.Apply(f, entity);
            }

            ConfigureOwnerAndArea(f, entity, owner, source, element, damageOverride, targetMaskOverride);
            ApplyRadiusUpgrade(f, entity, owner);

            f.AddOrGet<DestroyAfterTime>(entity, out var lifetime);
            lifetime->RemainingTime = ResolveDuration(f, owner, duration, source);

            return entity;
        }

        // AreaOwner is stamped unconditionally on every spawn, not gated behind AreaDamage/Vortex/
        // ExplodeOnDestroy (or any other specific component) - "who owns this and what damage source/
        // element does it count as" keeps growing new consumers (Vortex's crowd control,
        // ExplodeOnDestroy's blast, ...), and an allowlist here would only ever keep growing to match.
        // A decoy or anything else with nothing that reads AreaOwner simply carries an unused
        // component, same cost as any other optional data nothing happens to consume yet.
        //
        // Everything the area does beyond that is authored on the prototype's own AreaDamage by
        // default - damageOverride/targetMaskOverride are the exception: a caller that wants one
        // prototype reused with a different Damage/TargetMask per spawn (e.g. SpawnEntitySkillAction)
        // can supply one instead of needing a separate prototype per config. Null leaves the
        // prototype's own authored value untouched, same as before either existed. All AreaDamage-
        // specific handling below only runs when AreaDamage is actually present.
        private static void ConfigureOwnerAndArea(Frame f, EntityRef entity, EntityRef owner, DamageSource source,
            ElementType element, FP? damageOverride, DamageTargetMask? targetMaskOverride)
        {
            f.AddOrGet<AreaOwner>(entity, out var areaOwner);
            areaOwner->Owner = owner;
            areaOwner->Source = source;
            areaOwner->Element = element;

            if (f.Unsafe.TryGetPointer<AreaDamage>(entity, out var area) == false)
                return;

            if (damageOverride.HasValue == true)
            {
                area->Damage = damageOverride.Value;
            }

            if (targetMaskOverride.HasValue == true)
            {
                area->TargetMask = targetMaskOverride.Value;
            }

            // Seeded from InitialDelay (0 by default) so the area bites the moment it lands rather
            // than granting a free TickInterval of standing in it - and so a blast short enough to
            // live a single tick still fires at all. A telegraphed area can author a nonzero
            // InitialDelay to wait out its own windup instead.
            area->TickTimer = area->InitialDelay;
        }

        // SpawnRadiusUpgrade (see SpawnRadiusUpSkillAction, one .asset instance per hero) - grows
        // whatever's authored on the prototype's own PhysicsCollider3D by this
        // bonus, same per-shape math SpawnEntitySkillAction.ApplyScale uses for its own Scale field,
        // just off an owner-side upgrade instead of an authored value. One-time at spawn, not a live
        // regrow - an already-spawned area keeps whatever size it spawned at even if the owner's
        // upgrade is later revoked.
        private static void ApplyRadiusUpgrade(Frame f, EntityRef entity, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<SpawnRadiusUpgrade>(owner, out var upgrade) == false || upgrade->ScaleBonus <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
                return;

            FP scale = FP._1 + upgrade->ScaleBonus;

            switch (collider->Shape.Type)
            {
                case Shape3DType.Box:
                    collider->Shape.Box.Extents = collider->Shape.Box.Extents * scale;
                    break;

                case Shape3DType.Sphere:
                    collider->Shape.Sphere.Radius *= scale;
                    break;

                case Shape3DType.Capsule:
                    collider->Shape.Capsule.Radius *= scale;
                    collider->Shape.Capsule.Extent *= scale;
                    break;

                default:
                    Log.Error($"[Spawn] {entity} has a {collider->Shape.Type} collider - SpawnRadiusUpgrade only applies to Box, Sphere and Capsule");
                    break;
            }
        }

        // Only a skill's spawn stretches with the caster's SkillDurationMultiplier - a
        // weapon-spawned one (a grenade's lingering fire) keeps its authored duration, same rule
        // the damage multipliers follow. IncreaseDurationUpgrade stacks on top of that stat
        // multiplier rather than replacing it, same as SpawnRadiusUpgrade stacking on top of an
        // authored Scale.
        private static FP ResolveDuration(Frame f, EntityRef owner, FP duration, DamageSource source)
        {
            if (source != DamageSource.Skill)
                return duration;

            FP resolved = StatUtility.GetSkillDuration(f, owner, duration);

            if (f.Unsafe.TryGetPointer<IncreaseDurationUpgrade>(owner, out var upgrade) == true)
            {
                resolved *= FP._1 + upgrade->DurationBonus;
            }

            return resolved;
        }
    }
}
