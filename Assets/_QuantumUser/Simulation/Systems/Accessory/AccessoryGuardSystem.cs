namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives the world half of a dropped accessory: the Airborne -> Dropped landing transition, and
    // the walk-into-radius pickup that recovers it (see AccessoryGuard.qtn/docs/accessory-guard.md).
    // Structurally the same shape CurrencyOrbSystem uses, with one deliberate difference: whoever
    // picks it up, it always returns to its OWNER - so a teammate fetching it needs no
    // carried-accessory state, and co-op is a config flag rather than a second mechanic.
    //
    // Blocking itself is NOT here - that happens inline in DamageUtility.ApplyDamage
    // (AccessoryGuardUtility.TryBlock), since a hit has to be negated in the same call that would
    // otherwise apply it, not a tick later.
    [Preserve]
    public unsafe class AccessoryGuardSystem : SystemMainThreadFilter<AccessoryGuardSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            EntityRef owner = filter.DroppedAccessory->Owner;

            // Fell off the level. Unlike a coin - which is a bonus nobody misses if it drops into a
            // pit - a lost accessory is a permanently missing defensive resource, so this can never
            // be allowed to stand. Rescued rather than destroyed, and rescued by REPOSITIONING
            // rather than by clamping the arc: the pop deliberately allows big, awkward landings now
            // (PopVelocity.CanLandHigher), and clamping the throw to guarantee a safe landing would
            // flatten exactly the spatial mechanic this feature is built around.
            //
            // Same FallDeathHeight threshold and the same shared nearest-chunk math
            // PlayerFallSystem/EnemyFallSystem already use - seeded from the OWNER's position rather
            // than the fall point, because a pit is usually INSIDE a chunk's own footprint (see
            // FallRespawnUtility's note on interior drops), so resolving from where it fell can
            // hand back another spot over the same hole.
            if (TryRescueFromFall(f, filter.Entity, filter.Transform3D, owner) == true)
                return;

            // Debris from the killing block (see DroppedAccessory.Broken) - fire-and-forget. It
            // flies the same arc as a recoverable drop so the hit reads identically, but nothing
            // can collect it and no guard tracks it: it just lands, fires AccessoryBroken so the
            // destruction VFX plays at the resting point, and is gone. Checked FIRST, ahead of every
            // owner/guard rule below, because none of them apply to it - its owner's guard already
            // moved on to Broken the instant the hit landed.
            if (filter.DroppedAccessory->Broken == true)
            {
                if (f.Has<PopVelocity>(filter.Entity) == true)
                    return;

                f.Events.AccessoryBroken(owner, filter.Transform3D->Position);
                f.Destroy(filter.Entity);
                return;
            }

            // Orphaned - the owner left the match, or was never set. Nobody can ever collect this,
            // so it's pure clutter.
            if (owner == EntityRef.None || f.Exists(owner) == false)
            {
                f.Destroy(filter.Entity);
                return;
            }

            if (f.Unsafe.TryGetPointer<AccessoryGuard>(owner, out var guard) == false)
                return;

            // The owner's guard moved on without this entity (a Merchant repair/replacement already
            // restored them - see AccessoryGuardUtility.Restore, which destroys the collectible it
            // knows about). Defensive: a collectible that is somehow no longer the one its owner is
            // tracking must not stay pickable, or recovering it would silently re-equip an already
            // repaired accessory.
            if (guard->Accessory != filter.Entity)
            {
                f.Destroy(filter.Entity);
                return;
            }

            // Airborne -> Dropped, read straight off the pop arc's own marker rather than a second
            // timer: PopMotionSystem removes PopVelocity the instant the arc lands (see
            // PopVelocity.qtn). Until then the accessory is visibly in flight and deliberately NOT
            // collectible, so it can't be re-caught mid-air the same tick it popped off.
            if (f.Has<PopVelocity>(filter.Entity) == true)
            {
                if (guard->State != AccessoryGuardState.Airborne)
                    guard->State = AccessoryGuardState.Airborne;

                return;
            }

            if (guard->State == AccessoryGuardState.Airborne)
            {
                guard->State = AccessoryGuardState.Dropped;
                f.Events.AccessoryLanded(owner, filter.Transform3D->Position);
            }

            if (guard->State != AccessoryGuardState.Dropped)
                return;

            if (AccessoryGuardUtility.TryGetConfig(f, out AccessoryGuardConfig config) == false)
                return;

            EntityRef recoverer = ResolveRecoverer(f, filter.Entity, filter.Transform3D->Position, owner, config);

            if (recoverer == EntityRef.None)
                return;

            AccessoryGuardUtility.Recover(f, owner, recoverer, filter.Entity);
        }

        // Who, if anyone, is close enough to pick this up right now. Owner-only by default; with
        // AllowAllyRecovery any player can, which is what makes the accessory a TEAM problem rather
        // than a purely personal one - the spatial cost is unchanged (somebody still had to travel
        // to it), it just becomes a cost the team can share. It always returns to its owner either
        // way, so there is no carried-accessory state to model.
        //
        // Nearest wins, same tie-break CurrencyOrbSystem uses - only distinguishable when two
        // players are in range on the same tick.
        private static EntityRef ResolveRecoverer(Frame f, EntityRef collectible, FPVector3 position,
            EntityRef owner, AccessoryGuardConfig config)
        {
            if (config.PickupRadius <= FP._0)
                return EntityRef.None;

            if (config.AllowAllyRecovery == false)
                return IsWithinPickupRange(f, owner, position, config) ? owner : EntityRef.None;

            Span<EntityRef> players = stackalloc EntityRef[PlayerQueryUtility.MaxPlayers];
            int playerCount = PlayerQueryUtility.GatherPlayers(f, players);

            EntityRef closest = EntityRef.None;
            FP closestSqrDistance = default;

            for (int i = 0; i < playerCount; i++)
            {
                if (IsWithinPickupRange(f, players[i], position, config) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(players[i], out var playerTransform) == false)
                    continue;

                FP sqrDistance = (playerTransform->Position - position).SqrMagnitude;

                if (closest != EntityRef.None && sqrDistance >= closestSqrDistance)
                    continue;

                closest = players[i];
                closestSqrDistance = sqrDistance;
            }

            return closest;
        }

        private static bool IsWithinPickupRange(Frame f, EntityRef player, FPVector3 position, AccessoryGuardConfig config)
        {
            if (player == EntityRef.None)
                return false;

            // A Downed/KO player can't reach for anything - without this they'd silently vacuum up an
            // accessory while collapsed on top of it, same reasoning every other interaction in this
            // codebase gates on IsIncapacitated.
            if (PlayerLifeStateUtility.IsIncapacitated(f, player) == true)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(player, out var playerTransform) == false)
                return false;

            FP radius = config.PickupRadius;

            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == true)
                radius *= stats->PickupRangeMultiplier;

            if (radius <= FP._0)
                return false;

            return (playerTransform->Position - position).SqrMagnitude <= radius * radius;
        }

        // Returns true when a rescue happened this tick, so the caller skips the rest of its own
        // logic for one tick and picks up next tick with the accessory already somewhere valid.
        private static bool TryRescueFromFall(Frame f, EntityRef entity, Transform3D* transform, EntityRef owner)
        {
            if (f.RuntimeConfig.LevelConfig.IsValid == false)
                return false;

            LevelConfig levelConfig = f.FindAsset(f.RuntimeConfig.LevelConfig);

            if (transform->Position.Y >= levelConfig.FallDeathHeight)
                return false;

            // The owner is by definition standing somewhere valid and reachable (if THEY fell too,
            // PlayerFallSystem has already respawned them), which is what makes their position the
            // reliable seed. Falls back to the accessory's own position only when the owner is gone,
            // in which case it's about to be destroyed as orphaned anyway.
            FPVector3 seed = f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform)
                ? ownerTransform->Position
                : transform->Position;

            FPVector3 rescued = FallRespawnUtility.ResolveNearestChunkRespawnPosition(f, seed, levelConfig);
            transform->Position = rescued;

            // Re-seeded rather than removed, so the accessory finishes through the NORMAL landing
            // path (PopMotionSystem drops it the last few units onto real ground, removes
            // PopVelocity, and this system's own Airborne -> Dropped transition and AccessoryLanded
            // event fire exactly as they would for any other drop). Zeroed velocity means it falls
            // straight down from the rescue point instead of resuming its old trajectory back over
            // the pit; OriginGroundY is re-anchored to here so the climb rules judge it against
            // where it now is, not the ledge it fell from.
            if (f.Unsafe.TryGetPointer<PopVelocity>(entity, out var pop) == true)
            {
                pop->Velocity = FPVector3.Zero;
                pop->OriginGroundY = rescued.Y;
            }
            else
            {
                // Already settled and then fell (pushed off by something else) - it has no arc left
                // to finish, so ground it directly.
                GroundOffsetUtility.Apply(f, entity);
            }

            Log.Debug($"[Accessory] {entity} fell below FallDeathHeight={levelConfig.FallDeathHeight} - rescued to {rescued}");
            return true;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public DroppedAccessory* DroppedAccessory;
            public Transform3D* Transform3D;
        }
    }
}
