namespace Quantum
{
    using Photon.Deterministic;

    // Break side of a Breakable prop (see Breakable.qtn). TryBreak is called from
    // DamageUtility.ApplyDamage's non-Enemy death branch (the moment a Breakable's Health hits 0) as
    // a bolt-on TryGetPointer check, BEFORE the branch's usual f.Destroy - it returns true to tell
    // that branch to leave the entity alive (the whole point of a Breakable: the husk persists so its
    // View can show a broken state), false for anything that isn't an unbroken Breakable so the
    // normal destroy path runs unchanged.
    public static unsafe class BreakableUtility
    {
        // Returns true if this entity was an unbroken Breakable and has now been broken (caller must
        // then NOT destroy it), false otherwise (caller proceeds with its normal death handling).
        public static bool TryBreak(Frame f, EntityRef entity, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Breakable>(entity, out var breakable) == false)
                return false;

            if (breakable->Broken == true)
                return false; // already broken - collider's already gone, loot already dropped

            breakable->Broken = true;

            // Drop the prop out of the physics world so the husk is walk-through and can't be hit
            // again - same idiom EnemySystem.OnEnemyDied uses for a lingering dead enemy body.
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == true)
            {
                collider->Enabled = false;
            }

            FPVector3 position = FPVector3.Zero;
            if (f.Unsafe.TryGetPointer<Transform3D>(entity, out var transform) == true)
            {
                position = transform->Position;
            }

            TrySpawnLoot(f, entity, position);

            f.Events.BreakableBroken(entity, position);

            return true;
        }

        // Optional - a breakable WALL carries no SpawnOnBreak and drops nothing, this is a no-op for
        // it. Rolls the referenced BreakLootData table: every entry whose shared-Talent requirement
        // is satisfied and whose chance rolls drops Count pickups, each popped out on its own scatter
        // arc via the shared OrbSpawnUtility.SpawnWithPop and stamped with the entry's Value.
        private static void TrySpawnLoot(Frame f, EntityRef entity, FPVector3 position)
        {
            if (f.Unsafe.TryGetPointer<SpawnOnBreak>(entity, out var spawnOnBreak) == false)
                return;

            if (spawnOnBreak->Loot.IsValid == false)
                return;

            BreakLootData loot = f.FindAsset(spawnOnBreak->Loot);

            if (loot.Drops == null)
                return;

            for (int i = 0; i < loot.Drops.Length; i++)
            {
                BreakDrop drop = loot.Drops[i];

                if (TalentUtility.IsSatisfied(f, drop.Requirement) == false)
                    continue;

                // Chance <= 0 means "unauthored" -> always drops, same convention as
                // ChunkSpawnConfig.SpawnEntityWithRequirement.Chance.
                if (drop.Chance > FP._0 && DamageUtility.RollChance(f, drop.Chance) == false)
                    continue;

                if (drop.Prototype.Id.IsValid == false)
                {
                    Log.Debug($"[Breakable] {entity} loot entry {i} satisfied but no Prototype assigned - skipping");
                    continue;
                }

                int count = drop.Count > 0 ? drop.Count : 1;

                for (int c = 0; c < count; c++)
                {
                    EntityRef pickup = f.Create(drop.Prototype);
                    OrbSpawnUtility.SpawnWithPop(f, pickup, position, loot.MinSpawnOffset, loot.MaxSpawnOffset,
                        loot.PopHorizontalBurstSpeed, loot.PopVerticalBurstSpeed);
                    StampValue(f, pickup, drop.Value);

                    f.AddOrGet<DestroyAfterTime>(pickup, out var destroy);
                    destroy->RemainingTime = loot.OrbLifetime;
                }
            }
        }

        // One Value field on a BreakDrop feeds whichever pickup type it spawned - a CurrencyOrb's
        // credited amount, or a HealthOrb's heal FRACTION (0.25 = 25% of the collector's max health,
        // see HealthOrb.qtn) - so the drop table doesn't need a separate field per pickup kind. A
        // prototype that carries neither (some future pickup) just isn't stamped here.
        private static void StampValue(Frame f, EntityRef pickup, FP value)
        {
            if (f.Unsafe.TryGetPointer<CurrencyOrb>(pickup, out var currency) == true)
            {
                currency->Value = value;
                return;
            }

            if (f.Unsafe.TryGetPointer<HealthOrb>(pickup, out var healthOrb) == true)
            {
                healthOrb->HealPercent = value;
            }
        }
    }
}
