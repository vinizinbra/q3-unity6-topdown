namespace Quantum
{
    // Read side of DespawnIntent (see that component) - one place every on-death/on-destroy hook asks
    // "was this a genuine death, or a deliberate housekeeping removal?" instead of each re-deriving
    // its own answer.
    public static unsafe class DespawnIntentUtility
    {
        // True unless something explicitly stamped a non-death reason - so every pre-existing destroy
        // site in the codebase keeps triggering death effects exactly as it did before this existed.
        public static bool ShouldTriggerDeathEffects(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<DespawnIntent>(entity, out var intent) == false)
                return true;

            return intent->Reason == EntityDespawnReason.Destroyed;
        }

        // Stamp-then-destroy in one call, so a caller can't stamp and then forget to destroy (or
        // destroy first and stamp a dead entity).
        public static void DespawnSilently(Frame f, EntityRef entity, EntityDespawnReason reason)
        {
            if (entity == EntityRef.None || f.Exists(entity) == false)
                return;

            f.AddOrGet<DespawnIntent>(entity, out var intent);
            intent->Reason = reason;

            f.Destroy(entity);
        }
    }
}
