namespace Quantum
{
    using System;
    using Photon.Deterministic;

    // Finds players (and whatever else shares their physics layer) WITHOUT a physics query.
    //
    // Every "is a player near this" check in this project used to go through
    // Physics3D.OverlapShape on the Player layer mask. Each of those allocates a fresh
    // HitCollection3D on Quantum's frame heap (the UnsafeUtility.Malloc traffic visible under
    // EnemySystem/CurrencyOrbSystem/ChestSystem in the profiler) and pays full broadphase setup -
    // to find, at most, four players. EnemyLifecycleSystem.IsCloseToAnyPlayer already made the
    // opposite call ("for 2-4 players a direct compare beats query/layer-mask setup overhead") but
    // kept it private, so every other consumer went back to the query. This is that same idea,
    // shared, and it is now the single place that knows what "a player" means.
    //
    // TWO DIFFERENT CANDIDATE SETS, deliberately not merged:
    //
    //   Real players (GatherPlayers/IsAnyPlayerWithinFlatRange) - PlayerLink entities only. For
    //   anything FRIENDLY that means an actual person: orb pickup, chest opening, currency.
    //
    //   Player physics layer (TryFindNearestOnPlayerLayer/CountOnPlayerLayerInRadius/
    //   GatherOnPlayerLayer) - PlayerLink entities PLUS Sentry entities, because Lux's
    //   Sentry.prefab is authored on layer 7 ("Player") without a PlayerLink, so an enemy
    //   genuinely targets one today and EnemyDecisionUtility.TargetCountScore genuinely counts
    //   one. Anything enemy-facing has to keep seeing them.
    //
    //   INVARIANT: anything authored onto the Player physics layer must be represented in Scan
    //   below, or an enemy will silently stop seeing it. A Decoy is NOT in that set -
    //   Decoy.prefab sits on layer 0 (Default) despite what Decoy.qtn's own comment claims, so the
    //   layer-mask queries this replaces never returned one either; decoys are reached exclusively
    //   through EnemyMovementUtility.TryFindNearestDecoy's own component scan.
    //
    // The layerMask parameter is not decoration - it is what reproduces dash i-frames. DashSkillData
    // swaps a dashing player's PhysicsCollider3D.Layer to IgnoreProjectile for the dash's duration,
    // which is exactly why a GetPlayerLayerMask query misses them while a
    // GetPlayerIncludingDashingLayerMask one still finds them. Every candidate is tested against the
    // caller's mask using its own live collider layer, so that behavior survives unchanged.
    public static unsafe class PlayerQueryUtility
    {
        // Quantum.Input.MAX_COUNT, same constant PlayerClusterDirectorUtility already pins to.
        public const int MaxPlayers = PlayerClusterDirectorUtility.MaxPlayers;

        // 4 players + headroom for their live sentries (LuxScrapCollector.MaxActiveSentries is
        // per-Lux and data-driven, so there is no exact bound to derive). Only GatherOnPlayerLayer's
        // caller-supplied span is bounded by this - CountOnPlayerLayerInRadius and
        // TryFindNearestOnPlayerLayer scan every candidate regardless of how many there are.
        public const int MaxPlayerLayerCandidates = 16;

        // ---------------------------------------------------------------------------------------
        // Real players (PlayerLink)
        // ---------------------------------------------------------------------------------------

        // Fills every live player entity into the caller's buffer; returns the count. Deliberately
        // unbounded by range - the callers (orb/chest pickup) each apply their OWN per-player radius
        // (CharacterStats.PickupRangeMultiplier), which is why they used to run the physics query at
        // a padded 8x radius and then re-test center distance anyway. With <= 4 candidates the
        // prefilter earns nothing.
        public static int GatherPlayers(Frame f, Span<EntityRef> buffer)
        {
            int count = 0;
            var filtered = f.Filter<PlayerLink, Transform3D>();

            while (count < buffer.Length && filtered.Next(out EntityRef entity, out PlayerLink _, out Transform3D _) == true)
            {
                buffer[count] = entity;
                count++;
            }

            return count;
        }

        // Flat (XZ-only) proximity test against real players - EnemyLifecycleSystem's own relevance
        // check, which is where this whole utility's approach came from.
        public static bool IsAnyPlayerWithinFlatRange(Frame f, FPVector3 position, FP range)
        {
            FP rangeSqr = range * range;
            var filtered = f.Filter<PlayerLink, Transform3D>();

            while (filtered.Next(out EntityRef _, out PlayerLink _, out Transform3D transform) == true)
            {
                if (EnemyMovementUtility.FlatSqrDistance(position, transform.Position) <= rangeSqr)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // Player physics layer (PlayerLink + Sentry) - what an enemy sees
        // ---------------------------------------------------------------------------------------

        // Closest candidate within range, or EntityRef.None. Replaces
        // EnemyMovementUtility.TryFindNearestPlayer's OverlapShape + hits.Sort(origin) - taking the
        // minimum squared distance directly is the same result sort-then-take-first produced.
        public static bool TryFindNearestOnPlayerLayer(Frame f, FPVector3 origin, FP range, int layerMask,
            bool skipIncapacitated, out EntityRef entity)
        {
            Scan(f, origin, range, layerMask, skipIncapacitated, default, out _, out entity);
            return entity != EntityRef.None;
        }

        public static int CountOnPlayerLayerInRadius(Frame f, FPVector3 center, FP radius, int layerMask, bool skipIncapacitated)
        {
            Scan(f, center, radius, layerMask, skipIncapacitated, default, out int count, out _);
            return count;
        }

        // Fills the caller's buffer (stackalloc it at MaxPlayerLayerCandidates) and returns how many
        // were WRITTEN - candidates past the buffer's capacity are dropped, not counted, so a caller
        // iterating [0, result) never reads past what it asked for. Positions are deliberately not
        // returned: every caller that needs one already resolves Transform3D off the EntityRef, the
        // same way it did off Hit3D.Entity before.
        public static int GatherOnPlayerLayer(Frame f, FPVector3 origin, FP range, int layerMask, Span<EntityRef> buffer)
        {
            Scan(f, origin, range, layerMask, skipIncapacitated: false, buffer, out int count, out _);
            return Math.Min(count, buffer.Length);
        }

        // ---------------------------------------------------------------------------------------

        // The single candidate walk every player-layer query above shares. Reproduces what
        // Physics3D.OverlapShape(origin, sphere(range), layerMask) returned, candidate for
        // candidate:
        //   - an entity with no (or a disabled) PhysicsCollider3D is invisible to an overlap query,
        //     so it is skipped here too;
        //   - membership is tested against the entity's own live collider layer, so a dashing
        //     player is included/excluded exactly as the mask says (see the class comment);
        //   - the range test adds the candidate's own collider radius, because an overlap is a
        //     COLLIDER test - a player whose center sits at range + radius still overlapped the
        //     query sphere, and a bare center test would silently shrink every detection/engage
        //     range that doesn't re-check distance itself.
        // count is every match found (never capped by the buffer); buffer may be empty when the
        // caller only wants the count or the nearest.
        private static void Scan(Frame f, FPVector3 origin, FP range, int layerMask, bool skipIncapacitated,
            Span<EntityRef> buffer, out int count, out EntityRef nearest)
        {
            count = 0;
            nearest = EntityRef.None;
            FP nearestSqrDistance = default;

            var players = f.Filter<PlayerLink, Transform3D>();

            while (players.Next(out EntityRef entity, out PlayerLink _, out Transform3D transform) == true)
            {
                Consider(f, entity, transform.Position, origin, range, layerMask, skipIncapacitated,
                    buffer, ref count, ref nearest, ref nearestSqrDistance);
            }

            var sentries = f.Filter<Sentry, Transform3D>();

            while (sentries.Next(out EntityRef entity, out Sentry _, out Transform3D transform) == true)
            {
                Consider(f, entity, transform.Position, origin, range, layerMask, skipIncapacitated,
                    buffer, ref count, ref nearest, ref nearestSqrDistance);
            }
        }

        private static void Consider(Frame f, EntityRef entity, FPVector3 position, FPVector3 origin, FP range,
            int layerMask, bool skipIncapacitated, Span<EntityRef> buffer,
            ref int count, ref EntityRef nearest, ref FP nearestSqrDistance)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false || collider->Enabled == false)
                return;

            if ((layerMask & (1 << collider->Layer)) == 0)
                return;

            if (skipIncapacitated == true && PlayerLifeStateUtility.IsIncapacitated(f, entity) == true)
                return;

            FP reach = range + EnemyMovementUtility.ResolveShapeRadius(collider->Shape);
            FP sqrDistance = (position - origin).SqrMagnitude;

            if (sqrDistance > reach * reach)
                return;

            if (count < buffer.Length)
                buffer[count] = entity;

            if (nearest == EntityRef.None || sqrDistance < nearestSqrDistance)
            {
                nearest = entity;
                nearestSqrDistance = sqrDistance;
            }

            count++;
        }
    }
}
