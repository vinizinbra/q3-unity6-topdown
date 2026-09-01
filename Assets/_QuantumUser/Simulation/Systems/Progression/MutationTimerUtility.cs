namespace Quantum
{
    using Photon.Deterministic;

    // Per-player, per-tick bookkeeping for the Rift Mutations that need a real clock (Pressure
    // Cooker's safe-time streak, Scavenger Rush's collection window).
    //
    // Deliberately NOT a system of its own: StatusEffectSystem already iterates exactly the set of
    // entities these apply to (every player carries StatusEffects and CharacterStats), and it is
    // already the established home for CharacterStats-adjacent timers - Last Stand's cooldown ticks
    // on the very same line. A second system would iterate the same entities again for no gain.
    //
    // Everything here advances off f.DeltaTime, the deterministic simulation step - never wall-clock
    // time, never a View-side timer - so every client computes the identical value on the identical
    // tick.
    public static unsafe class MutationTimerUtility
    {
        public static void Tick(Frame f, EntityRef entity, CharacterStats* stats)
        {
            TickPressureCooker(f, stats);
            TickScavengerWindow(f, entity, stats);
        }

        // Pressure Cooker - accumulates uninterrupted time. Reset to 0 by the damage reactions, not
        // here, so this half only ever counts up; the bonus itself is derived from this value on
        // read (MutationModifierUtility.ResolvePressureCookerBonus) rather than stored, so there is
        // exactly one place the "per full second, capped" rule lives.
        //
        // Stops accumulating once the streak is long enough to have reached the cap, purely so the
        // value can't grow without bound over a long run.
        private static void TickPressureCooker(Frame f, CharacterStats* stats)
        {
            if (stats->PressureCookerDamagePerSecond <= FP._0 || stats->PressureCookerMaxBonus <= FP._0)
                return;

            FP secondsToCap = stats->PressureCookerMaxBonus / stats->PressureCookerDamagePerSecond;

            if (stats->SafeTimeSeconds >= secondsToCap + FP._1)
                return;

            stats->SafeTimeSeconds += f.DeltaTime;
        }

        // Scavenger Rush - the collection window closing. The counter is incremented by the pickup
        // reaction; this is only the deadline running out, which resets the streak so a slow trickle
        // of pickups can never accumulate into a trigger.
        private static void TickScavengerWindow(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (stats->ScavengerWindowRemaining <= FP._0)
                return;

            stats->ScavengerWindowRemaining -= f.DeltaTime;

            if (stats->ScavengerWindowRemaining > FP._0)
                return;

            stats->ScavengerWindowRemaining = FP._0;
            stats->ScavengerPickupCount = 0;
        }
    }
}
