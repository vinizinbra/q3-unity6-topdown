namespace Quantum
{
    using Photon.Deterministic;

    // Static per-projectile config. The Projectile component holds an AssetRef to this and reads it
    // as it flies, rather than copying these onto every spawned entity. Deliberately says nothing
    // about where a shot spawns from - that's the firing side's call (WeaponDataAsset,
    // ProjectileSkillData, ProjectileDeliveryData each own their own SpawnAnchor/SpawnOffset), since
    // the same reusable ProjectileData can be fired multiple different ways.
    public class ProjectileDataAsset : AssetObject
    {
        public AssetRef<EntityPrototype> Prototype;

        [ExpandableAsset] public AssetRef<ProjectileMovementData> Movement;
        [ExpandableAsset] public AssetRef<ProjectileHitData> Hit;

        public FP Lifetime = 3;

        // Zero (the default) preserves every existing projectile's exact current behavior - an
        // infinitely-thin Raycast each tick. Above zero, ProjectileSystem sweeps a sphere of this
        // radius instead - not just more forgiving of a near-miss, but the only way to catch a target
        // the projectile spawned already overlapping (a plain Raycast never reports a hit against a
        // collider its own origin starts inside of, since it only detects crossing a surface - a
        // point-blank spawn, e.g. FanProjectileDeliveryData fired right after a Leap lands on the
        // target, can silently whiff every time for exactly this reason).
        public FP HitRadius;

        // Seconds the projectile sits inert (no movement, no aging, hidden - see ProjectileView)
        // after spawning before it actually starts. Zero fires immediately, the default.
        public FP SpawnDelay;

        // Zero (the default) disables this - the projectile only ever expires by Lifetime, same as
        // before this field existed. Above zero, ProjectileSystem.TryExpire fires as soon as
        // Projectile.TraveledDistance reaches this, whichever of the two conditions comes first -
        // useful for a shot that's meant to activate at a fixed range regardless of how long that
        // takes (e.g. Kai's vortex bolt), rather than backing the range into Lifetime and an assumed
        // constant Speed.
        public FP MaxDistance;
    }
}
