namespace Quantum
{
    using Photon.Deterministic;

    // Overkill (Rift Mutation) - damage dealt beyond a killed enemy's remaining health is partly
    // re-dealt as a blast at the corpse, so a heavily over-tuned hit spills into the pack instead of
    // being wasted on a target that was already dead.
    //
    // Deliberately a small utility rather than a system: it has no per-tick state, and the one moment
    // it cares about (a kill, with the excess still knowable) is a single point inside
    // DamageUtility.ApplyDamage.
    public static unsafe class OverkillUtility
    {
        // overkillDamage is captured by ApplyDamage before CheatDeath/clamping can destroy it - see
        // its call site. sourceWasChained is that hit's own isChainedExplosion flag.
        public static void TryDetonate(Frame f, EntityRef target, EntityRef owner, DamageSource source,
            FP overkillDamage, bool sourceWasChained)
        {
            // No excess, no explosion - a kill that landed exactly on the last point of health is not
            // an overkill, and the design calls this out explicitly.
            if (overkillDamage <= FP._0)
                return;

            // RECURSION BRAKE. An Overkill blast is flagged as a chained explosion (below), and a
            // chained explosion can never produce another one. Without this, one blast could kill a
            // second enemy with excess damage, which would blast again, and so on - a chain bounded
            // only by how densely packed the pack happened to be.
            //
            // Reuses the exact flag Pixie's Chain Reaction already terminates on rather than adding a
            // depth counter, so both mechanics share one definition of "this hit is already a
            // knock-on".
            if (sourceWasChained == true)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            if (stats->OverkillConversion <= FP._0 || stats->OverkillRadius <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            FP explosionDamage = overkillDamage * stats->OverkillConversion;

            if (explosionDamage <= FP._0)
                return;

            // Enemies only: the blast is a payout for a kill, not a hazard to stand near. Also skips
            // the corpse itself for free - ApplyDamageInRadius refuses a target with no health left.
            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, stats->OverkillRadius, owner,
                explosionDamage, source, DamageTargetMask.Enemies,
                isChainedExplosion: true, isExplosion: true);

            f.Events.WeaponExplosionReleased(owner, transform->Position, stats->OverkillRadius);

            Log.Debug($"[RiftMutation] Overkill: {owner} killed {target} with {overkillDamage} excess -> {explosionDamage} blast at radius {stats->OverkillRadius}");
        }
    }
}
