namespace Quantum
{
    using Photon.Deterministic;

    // Both halves of the generic Priority Target behavior - see PriorityTarget.qtn for the full shape.
    // TrySetPriorityTarget is called once from DamageUtility.ApplyDamage, right after a genuine weapon
    // hit resolves; TryGetValidPriorityTarget is called by any consumer wanting to ask "does this owner
    // currently have a priority target I should prefer over my own normal targeting" (SentryBarrelSystem
    // today).
    public static unsafe class PriorityTargetUtility
    {
        // No-op for any owner without the upgrade, whose currently-equipped weapon's Element doesn't
        // match, or targeting something that isn't a real Enemy. Overwrites the owner's single
        // PriorityTarget slot unconditionally - refresh-on-same-target and replace-on-different-target
        // both fall out of this for free, since an owner only ever has one slot. For an explosion or
        // any other hit that damages several enemies in one tick, each qualifying ApplyDamage call
        // reaches here in the SAME deterministic order the damage pipeline already iterates its
        // targets in, and each overwrites the last - i.e. "last valid processed target wins", the
        // simplest rule consistent with that existing (already-deterministic) iteration order, with no
        // extra priority logic layered on top.
        public static void TrySetPriorityTarget(Frame f, EntityRef owner, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<PriorityTargetUpgrade>(owner, out var upgrade) == false)
                return;

            if (f.Has<Enemy>(target) == false)
                return;

            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return;

            if (f.FindAsset(weapon->WeaponData).Element != upgrade->RequiredElement)
                return;

            f.AddOrGet<PriorityTarget>(owner, out var priority);

            bool changed = priority->Target != target;

            priority->Target = target;
            priority->Remaining = upgrade->Duration;

            if (changed == true)
            {
                f.Events.PriorityTargetSet(owner, target);
            }
        }

        // A PRIORITY OVERRIDE, never forced targeting - true only if owner has an active, still-valid
        // priority target that ALSO sits within the caller's own range and passes the caller's own
        // validity rules (same Dead-phase/Invulnerable gate EnemyMovementUtility.TryFindNearestEnemy
        // already applies to every candidate it considers, so a priority target is held to exactly the
        // same standard as a normally-found one - never a looser one). False leaves the caller free to
        // fall back to its own normal target-selection unchanged.
        public static bool TryGetValidPriorityTarget(Frame f, EntityRef owner, FPVector3 position, FP range, out EntityRef target)
        {
            target = EntityRef.None;

            if (f.Unsafe.TryGetPointer<PriorityTarget>(owner, out var priority) == false
                || priority->Remaining <= FP._0 || priority->Target == EntityRef.None)
                return false;

            EntityRef candidate = priority->Target;

            if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var transform) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == false || enemy->Phase == EnemyActionPhase.Dead)
                return false;

            if (f.Has<Invulnerable>(candidate) == true)
                return false;

            FPVector3 delta = transform->Position - position;
            FP flatSqrDistance = delta.X * delta.X + delta.Z * delta.Z;

            if (flatSqrDistance > range * range)
                return false;

            target = candidate;
            return true;
        }
    }
}
