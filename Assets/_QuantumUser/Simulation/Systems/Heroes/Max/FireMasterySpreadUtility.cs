namespace Quantum
{
    using Photon.Deterministic;

    // Shared Burn-spread helper for StatusSpreadOnDeath's two triggers - Burning Vengeance
    // (MaxVendettaSystem.OnEntityKilled, Vendetta-kill only) and Wildfire
    // (MaxFireMasteryReactionSystem.OnEntityKilled, any Burning death) - so the two Passive
    // Upgrades that compose onto the same component also share one application path instead of
    // each re-deriving it. See docs/max-vendetta-fire-mastery.md.
    public static unsafe class FireMasterySpreadUtility
    {
        // BurnIntensity is applied as a flat damage-per-tick value (not a percent-of-hit-damage the
        // way BurnEffectData/StatusEffectUtility.ComputeDotDamagePerTick scale off a triggering
        // hit) - neither spread trigger has a "hit" to scale off of, only a death, so
        // StatusSpreadOnDeath's authored BurnIntensity is the tick damage directly. Source is
        // DamageSource.Skill (Max's own passive effect, not a weapon hit) - see
        // StatusEffectUtility.ScaleDuration's own DamageSource.Skill convention.
        public static void SpreadBurn(Frame f, FPVector3 center, EntityRef owner, EntityRef exclude,
            FP radius, FP burnDuration, FP burnIntensity, int maxTargets)
        {
            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            var targets = AreaQueryUtility.FindEnemiesInRadius(f, center, radius, exclude, maxTargets);

            for (int i = 0; i < targets.Count; i++)
            {
                StatusEffectUtility.ApplyBurn(f, targets[i], burnDuration, burnIntensity, owner, DamageSource.Skill, config.TickInterval);
            }
        }
    }
}
