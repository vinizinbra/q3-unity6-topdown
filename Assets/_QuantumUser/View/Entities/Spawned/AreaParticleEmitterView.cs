using System;
using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Emits one or more particle layers across a spawned area's footprint (World 2's StickyPuddle: a
    // big black "oil splash" body plus small "oil bubbles" over it) and lets them outlive the entity
    // gracefully. Each layer's prefab is instantiated per spawn rather than living as a static child,
    // so pooled views stay clean - when the entity despawns every instance is detached and only STOPS
    // EMITTING; particles already alive finish their own lifetime, then the system destroys itself
    // (StopAction.Destroy).
    //
    // Sizes follow the collider the area actually ended up with (a per-spawn Scale - see
    // SpawnedEntitySpawner.ApplySpawnScale), the same read-back ColliderVisualScaleView does for a
    // mesh. Author every layer for a radius-1 area; this multiplies its Shape radius by the real
    // radius, and optionally its start size (by radius) and emission rate (by radius²).
    public class AreaParticleEmitterView : CustomQuantumEntityViewComponent
    {
        [Serializable]
        private class Layer
        {
            [Tooltip("Particle prefab authored for a radius-1 area (Shape = Circle laid flat). Instantiated per spawn.")]
            public ParticleSystem Prefab;

            [Tooltip("Emission rate AND burst counts grow with the circle's area (radius²) - constant density. Use for many small particles spread over the area.")]
            public bool ScaleEmissionWithArea = true;

            [Tooltip("Start size grows with the radius - same particle count, bigger particles. Use for a few large particles that form the area's body.")]
            public bool ScaleSizeWithRadius;

            [Tooltip("Start lifetime = the entity's remaining lifetime (DestroyAfterTime) at spawn ± LifetimeJitter, and Size over Lifetime is rebuilt to hold full size until the last ShrinkDuration seconds. Pair with a single burst at 0 and no rate, so a one-shot burst lasts as long as the hazard.")]
            public bool MatchEntityLifetime;

            [Tooltip("MatchEntityLifetime only - random ± seconds around the hazard's duration, per particle.")]
            public float LifetimeJitter = 0.5f;

            [Tooltip("MatchEntityLifetime only - seconds at the end of each particle's life spent scaling down to 0.")]
            public float ShrinkDuration = 0.5f;

            [Tooltip("MatchEntityLifetime only - seconds at the start of each particle's life spent scaling up from 0 (a pop-in). 0 = appears at full size.")]
            public float GrowInDuration;
        }

        private struct Instance
        {
            public ParticleSystem System;
            public float BaseShapeRadius;
            public ParticleSystem.MinMaxCurve BaseRate;
            public ParticleSystem.MinMaxCurve BaseSize;
            public ParticleSystem.Burst[] BaseBursts;
        }

        [SerializeField]
        private List<Layer> layers = new();

        [SerializeField, Tooltip("Offset above the ground point the particles are emitted at (keeps flat billboards off the ground mesh).")]
        private Vector3 localOffset = new Vector3(0f, 0.05f, 0f);

        // Short on purpose - a long ray from high above could land on an overhanging ledge instead.
        private const float GroundSnapRayHeight = 2f;
        private static int? _groundLayerMask;

        private readonly List<Instance> _instances = new();
        private bool _sized;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            Release();
            _sized = false;

            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].Prefab == null)
                {
                    LogHelper.Error("AreaParticleEmitterView", $"'{name}' layer {i} has no particle prefab assigned.", this);
                    continue;
                }

                ParticleSystem system = Instantiate(layers[i].Prefab, transform);
                system.transform.localPosition = localOffset;
                system.transform.localRotation = Quaternion.identity;

                // Held until sized, so nothing emits at the authored radius-1 size first. StopAction
                // must NOT be Destroy while held: a stopped system with no live particles destroys
                // itself on the spot - which is what killed every instance whose entity takes a few
                // ticks to settle (a trail drop spawned above the ground) before it ever played. It
                // only becomes Destroy in Release, once the view lets go of it.
                ParticleSystem.MainModule main = system.main;
                main.stopAction = ParticleSystemStopAction.None;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                _instances.Add(new Instance
                {
                    System = system,
                    BaseShapeRadius = system.shape.radius,
                    BaseRate = system.emission.rateOverTime,
                    BaseSize = main.startSize,
                    BaseBursts = ReadBursts(system.emission),
                });
            }

            TrySize(game);
        }

        // Initialize can land before the spawn's collider is readable; this is the retry.
        protected override void QUpdate(QuantumGame game)
        {
            if (_sized == false)
                TrySize(game);
        }

        public override void DeInitialize(QuantumGame game)
        {
            Release();
            base.DeInitialize(game);
        }

        public override void OnDestroy()
        {
            Release();
            base.OnDestroy();
        }

        // Detach and stop emitting - alive particles play out, then StopAction.Destroy cleans up.
        private void Release()
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                ParticleSystem system = _instances[i].System;
                if (system == null)
                    continue;

                // Never sized/played (despawned before it settled): nothing alive to play out.
                if (system.isPlaying == false && system.particleCount == 0)
                {
                    Destroy(system.gameObject);
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                main.stopAction = ParticleSystemStopAction.Destroy;
                system.transform.SetParent(null, true);
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            _instances.Clear();
        }

        private void TrySize(QuantumGame game)
        {
            if (_instances.Count == 0)
                return;

            // Predicted, same reasoning as ColliderVisualScaleView: a spawn created this tick has no
            // verified frame yet.
            Frame frame = game.Frames.Predicted;
            if (frame == null || frame.TryGet<PhysicsCollider3D>(_entityRef, out PhysicsCollider3D collider) == false)
                return;

            // Wait for GroundSettleSystem to land the entity: particles simulate in world space, so a
            // burst fired at the spawn height (blast centre, or the carrier's own pivot for a trail)
            // would hang there while the entity drops to the ground beneath it.
            if (frame.TryGet<GroundOffset>(_entityRef, out GroundOffset groundOffset) == true && groundOffset.Enabled == true)
                return;

            if (frame.TryGet<Transform3D>(_entityRef, out Transform3D transform3D) == false)
                return;

            float radius = ResolveRadius(collider.Shape);
            _sized = true;

            // The settled simulation position, not this view's interpolated transform (it may still be
            // catching up), snapped onto the rendered ground - the sim's ground height doesn't
            // necessarily match the mesh, same fix GroundWarningTelegraphManager.SnapToGround applies.
            Vector3 emitPosition = SnapToGround(transform3D.Position.ToUnityVector3()) + localOffset;

            int layerIndex = 0;
            for (int i = 0; i < _instances.Count; i++)
            {
                // _instances skips layers with no prefab - walk layers in step to keep the flags paired.
                while (layers[layerIndex].Prefab == null)
                    layerIndex++;

                Layer layer = layers[layerIndex++];
                Instance instance = _instances[i];

                // Defensive: something external destroyed it (scene unload, pooled view teardown).
                if (instance.System == null)
                    continue;

                instance.System.transform.position = emitPosition;
                // Random yaw so an evenly spread burst (Shape arc mode BurstSpread) isn't the same
                // pattern every spawn. Cosmetic only - horizontal billboards ignore it otherwise.
                instance.System.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

                ParticleSystem.ShapeModule shape = instance.System.shape;
                shape.radius = instance.BaseShapeRadius * radius;

                if (layer.ScaleEmissionWithArea == true)
                {
                    ParticleSystem.EmissionModule emission = instance.System.emission;
                    emission.rateOverTime = Scale(instance.BaseRate, radius * radius);

                    var bursts = new ParticleSystem.Burst[instance.BaseBursts.Length];
                    for (int b = 0; b < bursts.Length; b++)
                    {
                        bursts[b] = instance.BaseBursts[b];
                        bursts[b].count = Scale(instance.BaseBursts[b].count, radius * radius);
                    }

                    emission.SetBursts(bursts);
                }

                if (layer.ScaleSizeWithRadius == true)
                {
                    ParticleSystem.MainModule main = instance.System.main;
                    main.startSize = Scale(instance.BaseSize, radius);
                }

                if (layer.MatchEntityLifetime == true &&
                    frame.TryGet<DestroyAfterTime>(_entityRef, out DestroyAfterTime lifetime) == true &&
                    lifetime.RemainingTime > Photon.Deterministic.FP._0)
                {
                    float duration = lifetime.RemainingTime.AsFloat;

                    ParticleSystem.MainModule main = instance.System.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(
                        Mathf.Max(0.1f, duration - layer.LifetimeJitter), duration + layer.LifetimeJitter);

                    // The curve is normalized to each particle's own lifetime - built off the hazard's
                    // duration, so "the last ShrinkDuration seconds" is exact at the mean and within the
                    // jitter either side.
                    float safeDuration = Mathf.Max(duration, 0.01f);
                    float holdUntil = Mathf.Clamp01(1f - layer.ShrinkDuration / safeDuration);
                    // Grow-in can't run past the shrink - a very short hazard just pops up and back.
                    float growUntil = Mathf.Min(Mathf.Clamp01(layer.GrowInDuration / safeDuration), holdUntil);

                    var curve = growUntil > 0f
                        ? new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(growUntil, 1f), new Keyframe(holdUntil, 1f), new Keyframe(1f, 0f))
                        : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(holdUntil, 1f), new Keyframe(1f, 0f));

                    ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = instance.System.sizeOverLifetime;
                    sizeOverLifetime.enabled = true;
                    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);
                }

                instance.System.Play(true);
            }
        }

        private static Vector3 SnapToGround(Vector3 position)
        {
            _groundLayerMask ??= UnityEngine.LayerMask.GetMask("Ground");

            if (Physics.Raycast(position + Vector3.up * GroundSnapRayHeight, Vector3.down, out RaycastHit hit,
                    GroundSnapRayHeight * 2f, _groundLayerMask.Value))
                position.y = hit.point.y;

            return position;
        }

        private static ParticleSystem.Burst[] ReadBursts(ParticleSystem.EmissionModule emission)
        {
            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            return bursts;
        }

        // The *Multiplier properties don't scale a TwoConstants range as a whole, so scale the curve
        // per mode instead (a random 0.15-0.35 start size stays a range).
        private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float factor)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    curve.constant *= factor;
                    break;

                case ParticleSystemCurveMode.TwoConstants:
                    curve.constantMin *= factor;
                    curve.constantMax *= factor;
                    break;

                default:
                    curve.curveMultiplier *= factor;
                    break;
            }

            return curve;
        }

        private static float ResolveRadius(Shape3D shape)
        {
            switch (shape.Type)
            {
                case Shape3DType.Sphere:
                    return shape.Sphere.Radius.AsFloat;

                case Shape3DType.Capsule:
                    return shape.Capsule.Radius.AsFloat;

                case Shape3DType.Box:
                    return Mathf.Min(shape.Box.Extents.X.AsFloat, shape.Box.Extents.Z.AsFloat);

                default:
                    return 1f;
            }
        }
    }
}
