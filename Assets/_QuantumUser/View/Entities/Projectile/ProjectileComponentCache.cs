using UnityEngine;

namespace Quantum
{
    // Component lists under one root, resolved ONCE when a pooled ProjectileView is created and
    // reused for every shot that view fires. The hierarchy of a projectile prefab never changes, so
    // paying for GetComponentsInChildren (and its array allocations) on every shot - three of them in
    // ProjectileVisualController.Initialize alone - bought nothing.
    //
    // Two things to keep in mind when reading from it:
    //  - It describes the PRISTINE hierarchy. Runtime-instantiated extras (the echo ghost and the
    //    weapon's extra particle, parented under the visual root at spawn) are not in it, so a shot that
    //    has any must fall back to scanning (ProjectileView.BuildSettings does).
    //  - While a shot is ending, trail pieces are detached from the root and still fading. They are in
    //    the cache but must not be touched by the "hide the body" sweeps - callers check IsAttached.
    public sealed class ProjectileComponentCache
    {
        public Renderer[] Renderers;
        public ParticleSystem[] Particles;
        public TrailRenderer[] Trails;
        public Light[] Lights;
        public HasLight[] GroundLights;
        public HasShadow[] GroundShadows;

        public static ProjectileComponentCache Build(Transform root)
        {
            return new ProjectileComponentCache
            {
                Renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true),
                Particles = root.GetComponentsInChildren<ParticleSystem>(includeInactive: true),
                Trails = root.GetComponentsInChildren<TrailRenderer>(includeInactive: true),
                Lights = root.GetComponentsInChildren<Light>(includeInactive: true),
                GroundLights = root.GetComponentsInChildren<HasLight>(includeInactive: true),
                GroundShadows = root.GetComponentsInChildren<HasShadow>(includeInactive: true),
            };
        }

        // True while `component` still sits under `root` - false once it was handed off to a
        // ParticleGracefulStop and unparented to fade where the shot landed.
        public static bool IsAttached(Component component, Transform root)
        {
            return component != null && component.transform.IsChildOf(root);
        }
    }
}
