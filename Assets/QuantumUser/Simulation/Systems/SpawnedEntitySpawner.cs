namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared by skills (SpawnEntitySkillAction) and projectile impacts (SpawnEntityEffectData) so
    // both drop things into the world the same way: a prefab plus how long it lives. What the
    // spawned thing then does to people is the prototype's own business - a prefab carrying an
    // AreaDamage hurts whoever stands in it, one without just sits there.
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
                ApplyGroundOffset(f, entity, transform);
            }

            ConfigureArea(f, entity, owner, source, element, damageOverride, targetMaskOverride);
            ApplyRadiusUpgrade(f, entity, owner);

            f.AddOrGet<DestroyAfterTime>(entity, out var lifetime);
            lifetime->RemainingTime = ResolveDuration(f, owner, duration, source);

            return entity;
        }

        // GroundOffset (see GroundOffset.qtn) - optional, so a prototype without it (most projectile
        // impacts, which already spawn at a resolved hit position) is untouched. Raycasts straight
        // down from the just-set XZ rather than trusting the caller's Y, since spawn positions here
        // are usually derived from a caster's own Transform3D (Sentry) or a hit point (Vortex), not
        // an already-ground-checked one. Snaps immediately unless the relevant direction's approach
        // rate is authored (descending reads FallGravityMultiplier, ascending reads FloatSpeed) -
        // only then is this worth spreading across ticks via SettlingToGround/GroundSettleSystem
        // instead of just placing the entity there once, up front.
        private static void ApplyGroundOffset(Frame f, EntityRef entity, Transform3D* transform)
        {
            if (f.Unsafe.TryGetPointer<GroundOffset>(entity, out var groundOffset) == false)
                return;

            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);

            if (EnemyMovementUtility.TryFindGroundHeight(f, transform->Position, groundLayerMask, out FP groundY) == false)
            {
                Log.Error($"[Spawn] {entity} has a GroundOffset but no ground was found beneath {transform->Position} - left at spawn Y");
                return;
            }

            FP targetY = groundY + groundOffset->Offset;
            FP approachRate = targetY < transform->Position.Y ? groundOffset->FallGravityMultiplier : groundOffset->FloatSpeed;

            if (approachRate <= FP._0)
            {
                transform->Position = new FPVector3(transform->Position.X, targetY, transform->Position.Z);
                return;
            }

            SettlingToGround settling = default;
            settling.TargetY = targetY;
            f.Add(entity, settling);
        }

        // Everything the area does is authored on the prototype's AreaDamage by default - owner,
        // source and element are the only things the spawn site knows that the prefab can't.
        // damageOverride/targetMaskOverride are the exception: a caller that wants one prototype
        // reused with a different Damage/TargetMask per spawner (e.g. SpawnEntitySkillAction) can
        // supply one instead of needing a separate prototype per config. Null leaves the
        // prototype's own authored value untouched, same as before either existed.
        //
        // AreaOwner is stamped for a Vortex too, not just AreaDamage - Kai's vortex has no damage of
        // its own (pure crowd control), but VortexSystem still needs to resolve who owns it, same
        // reason AreaDamageSystem does. The AreaDamage-specific overrides below only apply when
        // AreaDamage is actually present.
        private static void ConfigureArea(Frame f, EntityRef entity, EntityRef owner, DamageSource source,
            ElementType element, FP? damageOverride, DamageTargetMask? targetMaskOverride)
        {
            bool hasArea = f.Unsafe.TryGetPointer<AreaDamage>(entity, out var area) == true;
            bool hasVortex = f.Has<Vortex>(entity) == true;

            if (hasArea == false && hasVortex == false)
                return;

            f.AddOrGet<AreaOwner>(entity, out var areaOwner);
            areaOwner->Owner = owner;
            areaOwner->Source = source;
            areaOwner->Element = element;

            if (hasArea == false)
                return;

            if (damageOverride.HasValue == true)
            {
                area->Damage = damageOverride.Value;
            }

            if (targetMaskOverride.HasValue == true)
            {
                area->TargetMask = targetMaskOverride.Value;
            }

            // Zero so the area bites the moment it lands rather than granting a free TickInterval of
            // standing in it - and so a blast short enough to live a single tick fires at all.
            area->TickTimer = FP._0;
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
