namespace Quantum
{
    using Photon.Deterministic;

    // Generic "how close to the center of this area did the hit land" damage scaling - the reusable
    // normalized distance-to-center multiplier Focused Power (docs/rift-mutations.md) is built on.
    //
    // Deliberately hero- and skill-agnostic. It reads only HitEffectContext.AreaCenter/AreaRadius,
    // which HitEffectUtility's overlap paths already populate, so:
    //   - a skill with a real spatial area gets the falloff automatically, whatever hero owns it;
    //   - a skill with no meaningful area (a direct hit, a single-target cast) reports AreaRadius 0
    //     and receives exactly 1x, with no per-hero or per-skill check anywhere.
    //
    // Pure fixed-point math on values already in the frame, so it's fully deterministic.
    public static unsafe class SkillFocusUtility
    {
        // 1x at the rim, 1 + SkillCenterFocusBonus at the exact center, linear in between.
        //
        // Scoped to DamageSource.Skill: the whole point is rewarding accurate SKILL placement, and
        // letting it apply to a weapon-sourced explosion would silently hand the same bonus to every
        // grenade/explosive perk too.
        public static FP ResolveCenterFocusMultiplier(Frame f, ref HitEffectContext context)
        {
            if (context.Source != DamageSource.Skill || context.AreaRadius <= FP._0)
                return FP._1;

            if (f.Unsafe.TryGetPointer<CharacterStats>(context.Owner, out var stats) == false
                || stats->SkillCenterFocusBonus <= FP._0)
                return FP._1;

            FP distanceFraction = FPVector3.Distance(context.AreaCenter, context.Position) / context.AreaRadius;

            // Clamped rather than assumed in range: an overlap query catches an entity by its
            // COLLIDER, so a large target's center can legitimately sit slightly outside the sphere's
            // own radius and produce a fraction just over 1.
            FP centerCloseness = FP._1 - FPMath.Clamp01(distanceFraction);

            return FP._1 + stats->SkillCenterFocusBonus * centerCloseness;
        }
    }
}
