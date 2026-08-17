namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Drives the "hold the reticle on a barrel for BreakDelay seconds before it breaks" dwell (see
    // Breakable.qtn) - so a Breakable that a player is auto-targeting doesn't pop the instant a shot
    // lands, it fills a visible focus timer first (the reticle/target view reads FocusTimer/BreakDelay
    // to show progress). Runs right after AimSystem so each player's Aim.Target is this tick's fresh
    // value, and inside GameplaySystemGroup so the dwell freezes with everything else during a
    // paused upgrade screen.
    //
    // A Breakable only ever becomes a weapon-holder's Aim.Target within AimSystem.BreakableTargetRange
    // and only when no enemy is competing (strictly-lowest-priority fallback), so simply reading
    // Aim.Target here already inherits both gates for free - this system never has to re-check range
    // or hostiles.
    [Preserve]
    public unsafe class BreakableFocusSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            var aimers = f.Filter<Aim>();

            while (aimers.Next(out EntityRef aimer, out Aim aim))
            {
                // Only weapon-holders can auto-fire at (and so focus) a prop - AimSystem's own
                // fallback is gated the same way, but an enemy's Aim also exists purely for facing, so
                // guard here too.
                if (f.Has<Weapon>(aimer) == false)
                    continue;

                EntityRef target = aim.Target;

                if (target == EntityRef.None)
                    continue;

                if (f.Unsafe.TryGetPointer<Breakable>(target, out var breakable) == false)
                    continue;

                if (breakable->Broken == true || breakable->BreakDelay <= FP._0)
                    continue;

                // Another player already advanced this barrel's dwell this tick - don't double-count
                // (co-op focusing shouldn't break it N times faster; "BreakDelay seconds" means the
                // same for one player or four).
                if (breakable->LastTargetedFrame == f.Number)
                    continue;

                // Targeting lapsed (nobody aimed at it last tick) - restart the dwell from 0 rather
                // than resuming a stale partial fill.
                if (breakable->LastTargetedFrame != f.Number - 1)
                    breakable->FocusTimer = FP._0;

                breakable->LastTargetedFrame = f.Number;
                breakable->FocusTimer += f.DeltaTime;

                if (breakable->FocusTimer >= breakable->BreakDelay)
                    BreakableUtility.TryBreak(f, target, aimer);
            }
        }
    }
}
