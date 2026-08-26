namespace Quantum
{
    using Photon.Deterministic;

    // The whole Survival half of the Recoverable Accessory Guard (see AccessoryGuard.qtn/
    // docs/accessory-guard.md): seeding, blocking a hit, popping the accessory off into the world,
    // and recovering it. Every method here is hero-agnostic - none of them ever resolves which hero
    // owns the guard, let alone what the accessory visually is.
    //
    // The Break half (Merchant repair/replacement) lives in AccessoryServiceUtility, which funnels
    // back through Restore() below so all three ways back to full (pickup, repair, replacement)
    // share one implementation.
    public static unsafe class AccessoryGuardUtility
    {
        // Called from CharacterSystem.OnEntityPrototypeMaterialized for every PLAYER character (see
        // its own PlayerLink gate). Adds the component rather than requiring it on each hero's
        // prototype, so the mechanic is enabled or disabled entirely by whether
        // RuntimeConfig.AccessoryGuardConfig is assigned - no per-hero authoring, and no chance of
        // one hero's prototype silently missing it.
        public static void Seed(Frame f, EntityRef player)
        {
            // Said out loud rather than returned silently. Every symptom of an unseeded guard is
            // indirect - no worn visual, no HUD pips, no Merchant service, nothing blocks - so a
            // quiet return here reads as "the feature is broken" instead of "one field is unassigned".
            // Worth a line per spawning player (4 at most) to make that unmistakable.
            if (TryGetConfig(f, out AccessoryGuardConfig config) == false)
            {
                Log.Warn($"[Accessory] {player} spawned with no AccessoryGuard - RuntimeConfig.AccessoryGuardConfig " +
                         "is unassigned for THIS runtime config (note the menu path and each scene's own embedded " +
                         "debug config are separate copies). Nothing will block, drop or be repairable.");
                return;
            }

            if (config.BaseDurability == 0)
            {
                Log.Warn($"[Accessory] {player} spawned with no AccessoryGuard - {config.name} authors " +
                         "BaseDurability 0, which disables the mechanic entirely.");
                return;
            }

            f.AddOrGet<AccessoryGuard>(player, out var guard);

            guard->MaxDurability = config.BaseDurability;
            guard->CurrentDurability = config.BaseDurability;
            guard->State = AccessoryGuardState.Equipped;
            guard->Accessory = EntityRef.None;

            Log.Debug($"[Accessory] {player} seeded at {guard->CurrentDurability}/{guard->MaxDurability}");
        }

        // Called from DamageUtility.ApplyDamage, before ANY of its own resolution runs (see that
        // call site). Returns true when this hit is fully consumed by the guard - the caller then
        // returns immediately, so a blocked hit deals no damage, rolls no crit, procs no elemental
        // status, builds no Rage and fires no on-hit signal. A block is a genuine negation of
        // the hit, not a mitigation of it.
        //
        // Only ever fires while Equipped, which is what makes multi-hit sources self-limiting: the
        // first pellet of a shotgun blast pops the accessory off, and every later pellet in that
        // same tick finds State != Equipped and lands normally. No cooldown/i-frame window needed.
        public static bool TryBlock(Frame f, EntityRef target, EntityRef owner, FP damage)
        {
            if (f.Unsafe.TryGetPointer<AccessoryGuard>(target, out var guard) == false)
                return false;

            if (guard->State != AccessoryGuardState.Equipped || guard->CurrentDurability == 0)
                return false;

            guard->CurrentDurability--;

            FPVector3 position = f.Unsafe.TryGetPointer<Transform3D>(target, out var transform)
                ? transform->Position
                : FPVector3.Zero;

            // 0 durability left -> Broken. The accessory still visibly gets knocked off and flies
            // the same arc - the hit looked identical, after all - but as DEBRIS: never collectible,
            // never tracked on the guard, and destroyed the moment it lands (AccessoryGuardSystem),
            // which is where AccessoryBroken fires so the destruction VFX plays where it actually
            // came to rest rather than on the player. State flips to Broken right here regardless,
            // so the worn visual disappears on impact while the debris is still in the air.
            if (guard->CurrentDurability == 0)
            {
                guard->State = AccessoryGuardState.Broken;
                guard->Accessory = EntityRef.None;

                EntityRef debris = SpawnCollectible(f, target, position, broken: true);

                f.Events.AccessoryBlocked(target, owner, damage, guard->CurrentDurability, position);

                // Nothing is going to fly and land, so nothing would ever fire it - raise it here
                // instead, at the player, rather than losing the break VFX entirely.
                if (debris == EntityRef.None)
                    f.Events.AccessoryBroken(target, position);

                Log.Debug($"[Accessory] {target} blocked {damage} and BROKE (0/{guard->MaxDurability}), debris {debris}");
                return true;
            }

            EntityRef collectible = SpawnCollectible(f, target, position, broken: false);

            // Graceful degradation: with no prototype assigned there is nothing to walk back to, and
            // parking the player in Airborne forever would leave them permanently guardless with no
            // recoverable entity in the world. The durability point is still spent (the hit WAS
            // blocked), but the accessory stays worn rather than becoming unrecoverable - see the
            // Log.Error SpawnCollectible already raised.
            if (collectible == EntityRef.None)
            {
                f.Events.AccessoryBlocked(target, owner, damage, guard->CurrentDurability, position);
                return true;
            }

            guard->State = AccessoryGuardState.Airborne;
            guard->Accessory = collectible;

            f.Events.AccessoryBlocked(target, owner, damage, guard->CurrentDurability, position);

            Log.Debug($"[Accessory] {target} blocked {damage} -> {guard->CurrentDurability}/{guard->MaxDurability}, accessory popped off as {guard->Accessory}");
            return true;
        }

        // The generic collectible - ONE shared prototype for every hero (the View resolves which
        // sprite to render through DroppedAccessory.Owner, see DroppedAccessoryView).
        //
        // Landing point FIRST, arc second. A currency drop can be lobbed at a random ring point and
        // left to come down wherever it lands, because a coin lost to water or a pit costs nothing.
        // An accessory lost that way is a missing defensive resource, and correcting it after the
        // fact is a visible teleport - so the spot is validated as solid ground before anything is
        // thrown, and the arc is then solved exactly onto it.
        private static EntityRef SpawnCollectible(Frame f, EntityRef owner, FPVector3 position, bool broken)
        {
            if (f.RuntimeConfig.Prefabs.DroppedAccessoryPrototype.IsValid == false)
            {
                Log.Error("[Accessory] RuntimeConfig.Prefabs.DroppedAccessoryPrototype is not assigned - the hit was still blocked, but the accessory stays worn instead of popping off (see TryBlock's own degradation note)");
                return EntityRef.None;
            }

            if (TryGetConfig(f, out AccessoryGuardConfig config) == false)
                return EntityRef.None;

            EntityRef collectible = f.Create(f.RuntimeConfig.Prefabs.DroppedAccessoryPrototype);

            FPVector3 landing = ResolveLandingPosition(f, position, config);

            // The angle - not the velocity - is what varies per drop. Randomising velocity would
            // break the solved arc and land the accessory somewhere other than the spot just
            // validated, which is exactly the blind-throw behaviour this replaces.
            FP launchAngle = f.RNG->Next(config.MinLaunchAngle, config.MaxLaunchAngle);

            OrbSpawnUtility.SpawnWithPopTo(f, collectible, position, landing, launchAngle, config.CanLandOnHigherGround);

            f.AddOrGet<DroppedAccessory>(collectible, out var dropped);
            dropped->Owner = owner;
            dropped->Broken = broken;

            return collectible;
        }

        // Samples candidate spots in the configured ring and returns the first that actually has
        // Ground under it. Water is its own Unity layer, so a candidate over open water finds no
        // Ground-layer collider and is rejected by the same test that rejects a pit or the level
        // edge - no water-specific check needed, and any future non-standable surface is handled
        // for free by simply not being on the Ground layer.
        //
        // The returned point sits ON the ground it found (not at the owner's own Y), so the arc is
        // solved to where the accessory will actually come to rest.
        //
        // Falling back to the owner's own position is a genuine guarantee rather than a guess: they
        // are standing there, so it is solid, reachable ground by definition. Worst case the
        // accessory lands at their feet - undramatic, but never lost and never teleported.
        private static FPVector3 ResolveLandingPosition(Frame f, FPVector3 anchor, AccessoryGuardConfig config)
        {
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            int attempts = config.LandingSampleAttempts > 0 ? config.LandingSampleAttempts : 1;

            for (int i = 0; i < attempts; i++)
            {
                FPVector3 candidate = EnemyMovementUtility.RandomPositionInRing(f, anchor, config.MinDropOffset, config.MaxDropOffset);

                if (EnemyMovementUtility.TryFindGroundHeight(f, candidate, groundLayerMask, out FP groundY) == false)
                    continue;

                return new FPVector3(candidate.X, groundY, candidate.Z);
            }

            Log.Debug($"[Accessory] no valid landing spot found in {attempts} samples around {anchor} - dropping at the owner's feet");
            return anchor;
        }

        // Called from AccessoryGuardSystem once a player physically reaches a landed accessory.
        // Restores the state, not the durability - recovering a 2/3 accessory gives back a 2/3
        // accessory, which is exactly what makes the Merchant decision matter.
        //
        // recoverer may be a TEAMMATE rather than the owner (co-op, see
        // AccessoryGuardConfig.AllowAllyRecovery). It only ever affects the event payload: the
        // accessory always goes back to its owner, never to whoever picked it up, so there is no
        // "carrying someone else's hat" state to model.
        public static void Recover(Frame f, EntityRef owner, EntityRef recoverer, EntityRef collectible)
        {
            if (f.Unsafe.TryGetPointer<AccessoryGuard>(owner, out var guard) == false)
                return;

            guard->State = AccessoryGuardState.Equipped;
            guard->Accessory = EntityRef.None;

            FPVector3 position = f.Unsafe.TryGetPointer<Transform3D>(collectible, out var transform)
                ? transform->Position
                : FPVector3.Zero;

            f.Destroy(collectible);
            f.Events.AccessoryRecovered(owner, recoverer, position, guard->CurrentDurability);

            Log.Debug(recoverer == owner
                ? $"[Accessory] {owner} recovered their own accessory at {guard->CurrentDurability}/{guard->MaxDurability}"
                : $"[Accessory] {recoverer} returned {owner}'s accessory at {guard->CurrentDurability}/{guard->MaxDurability}");
        }

        // The single "back to full and worn" funnel, shared by a Merchant repair AND a replacement
        // (see AccessoryServiceUtility.TryPurchaseService) - there is deliberately no partial
        // restore anywhere in this file.
        //
        // Destroying any still-outstanding world collectible is what upholds the "never both worn
        // and lying on the floor" invariant (docs/accessory-guard.md): a player can walk into the
        // Merchant at 2/3 with their accessory still lying somewhere out in the level, and paying
        // for a repair has to reconcile that, not leave a ghost copy behind.
        public static void Restore(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<AccessoryGuard>(player, out var guard) == false)
                return;

            DestroyOutstandingCollectible(f, guard);

            guard->CurrentDurability = guard->MaxDurability;
            guard->State = AccessoryGuardState.Equipped;
            guard->Accessory = EntityRef.None;
        }

        // Also used by AccessoryGuardSystem when an owner stops existing - an ownerless accessory
        // can never be collected by anyone, so leaving it lying there is pure clutter.
        public static void DestroyOutstandingCollectible(Frame f, AccessoryGuard* guard)
        {
            if (guard->Accessory == EntityRef.None)
                return;

            if (f.Exists(guard->Accessory) == true)
                f.Destroy(guard->Accessory);

            guard->Accessory = EntityRef.None;
        }

        // How many durability points are missing - the single input both the service KIND and its
        // price are derived from (see AccessoryServiceUtility).
        public static int GetMissingDurability(AccessoryGuard* guard)
        {
            return guard->MaxDurability - guard->CurrentDurability;
        }

        public static bool TryGetConfig(Frame f, out AccessoryGuardConfig config)
        {
            config = null;

            if (f.RuntimeConfig.AccessoryGuardConfig.IsValid == false)
                return false;

            config = f.FindAsset(f.RuntimeConfig.AccessoryGuardConfig);
            return config != null;
        }
    }
}
