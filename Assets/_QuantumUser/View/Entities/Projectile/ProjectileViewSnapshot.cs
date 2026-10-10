using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
    // Implemented by sibling view components that keep per-use state of their own and need to clear
    // it when a pooled projectile view goes back to the pool (see ProjectileView.CompleteRelease).
    public interface IProjectilePoolPart
    {
        void ResetForPool();
    }

    // Pristine copy of everything a projectile view's hierarchy can be mutated into during one
    // shot, captured once right after the instance is created (before any view component has
    // initialized) and replayed when the view returns to ProjectileViewUpdater's pool.
    //
    // Why a generic snapshot rather than per-component resets: a projectile's look is rewritten
    // per WEAPON at spawn (ProjectileDataVisualsView tints particles, overrides lifetimes, swaps the
    // sprite, retimes the trail, recolors the ground light) and half of those writes are conditional
    // ("only if the override is > 0"), so a reused instance would otherwise leak the previous
    // weapon's value into the next shot. The visual controller also toggles renderers and emitters
    // while the shot flies. Restoring every writable property the project is known to touch, from
    // one place, is cheaper to keep correct than chasing each writer.
    //
    // Deliberately NOT restored for the view's own root: parent, pose and active state - the pool
    // owns those. Everything below the root is restored in full, including the visual root and the
    // trail pieces that were detached while fading (they must already be back home by then).
    public sealed class ProjectileViewSnapshot
    {
        private struct TransformState
        {
            public Transform Transform;
            public Transform Parent;
            public int SiblingIndex;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public bool Active;
        }

        private struct RendererState
        {
            public Renderer Renderer;
            public bool Enabled;
        }

        private struct SpriteState
        {
            public SpriteRenderer Renderer;
            public Sprite Sprite;
            public Color Color;
        }

        private struct TrailState
        {
            public TrailRenderer Renderer;
            public float Time;
            public float WidthMultiplier;
            public Gradient Gradient;
            public bool Emitting;
        }

        private struct BehaviourState
        {
            public Behaviour Behaviour;
            public bool Enabled;
        }

        private struct ParticleState
        {
            public ParticleSystem System;
            public ParticleSystem.MinMaxGradient StartColor;
            public ParticleSystem.MinMaxCurve StartLifetime;
            public ParticleSystem.MinMaxCurve RateOverDistance;
        }

        private struct LightState
        {
            public HasLight Light;
            public Color Color;
        }

        private readonly Transform _root;
        private readonly List<TransformState> _transforms = new List<TransformState>();
        private readonly List<RendererState> _renderers = new List<RendererState>();
        private readonly List<SpriteState> _sprites = new List<SpriteState>();
        private readonly List<TrailState> _trails = new List<TrailState>();
        private readonly List<BehaviourState> _behaviours = new List<BehaviourState>();
        private readonly List<ParticleState> _particles = new List<ParticleState>();
        private readonly List<LightState> _lights = new List<LightState>();

        private ProjectileViewSnapshot(Transform root)
        {
            _root = root;
        }

        public static ProjectileViewSnapshot Capture(Transform root)
        {
            var snapshot = new ProjectileViewSnapshot(root);

            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t == root)
                    continue;

                snapshot._transforms.Add(new TransformState
                {
                    Transform = t,
                    Parent = t.parent,
                    SiblingIndex = t.GetSiblingIndex(),
                    LocalPosition = t.localPosition,
                    LocalRotation = t.localRotation,
                    LocalScale = t.localScale,
                    Active = t.gameObject.activeSelf,
                });
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(includeInactive: true))
                snapshot._renderers.Add(new RendererState { Renderer = r, Enabled = r.enabled });

            foreach (SpriteRenderer s in root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                snapshot._sprites.Add(new SpriteState { Renderer = s, Sprite = s.sprite, Color = s.color });

            foreach (TrailRenderer tr in root.GetComponentsInChildren<TrailRenderer>(includeInactive: true))
            {
                var gradient = new Gradient();
                gradient.SetKeys(tr.colorGradient.colorKeys, tr.colorGradient.alphaKeys);
                gradient.mode = tr.colorGradient.mode;

                snapshot._trails.Add(new TrailState
                {
                    Renderer = tr,
                    Time = tr.time,
                    WidthMultiplier = tr.widthMultiplier,
                    Gradient = gradient,
                    Emitting = tr.emitting,
                });
            }

            foreach (Behaviour b in root.GetComponentsInChildren<Behaviour>(includeInactive: true))
                snapshot._behaviours.Add(new BehaviourState { Behaviour = b, Enabled = b.enabled });

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                snapshot._particles.Add(new ParticleState
                {
                    System = ps,
                    StartColor = ps.main.startColor,
                    StartLifetime = ps.main.startLifetime,
                    RateOverDistance = ps.emission.rateOverDistance,
                });
            }

            foreach (HasLight l in root.GetComponentsInChildren<HasLight>(includeInactive: true))
                snapshot._lights.Add(new LightState { Light = l, Color = l.LightColor });

            return snapshot;
        }

        // Must run while the pooled view's root is INACTIVE: that way re-enabling children below
        // fires no OnEnable (HasLight/HasShadow would otherwise acquire a ground blob just to
        // release it again a moment later) and nothing replays an emitter mid-restore.
        public void Restore()
        {
            foreach (TransformState s in _transforms)
            {
                Transform t = s.Transform;
                if (t == null)
                    continue;

                if (t.parent != s.Parent)
                    t.SetParent(s.Parent, worldPositionStays: false);

                t.SetSiblingIndex(s.SiblingIndex);
                t.localPosition = s.LocalPosition;
                t.localRotation = s.LocalRotation;
                t.localScale = s.LocalScale;
            }

            foreach (RendererState s in _renderers)
            {
                if (s.Renderer != null)
                    s.Renderer.enabled = s.Enabled;
            }

            foreach (SpriteState s in _sprites)
            {
                if (s.Renderer == null)
                    continue;

                s.Renderer.sprite = s.Sprite;
                s.Renderer.color = s.Color;
            }

            foreach (TrailState s in _trails)
            {
                if (s.Renderer == null)
                    continue;

                s.Renderer.Clear();
                s.Renderer.time = s.Time;
                s.Renderer.widthMultiplier = s.WidthMultiplier;
                s.Renderer.colorGradient = s.Gradient;
                s.Renderer.emitting = s.Emitting;
            }

            foreach (BehaviourState s in _behaviours)
            {
                if (s.Behaviour != null)
                    s.Behaviour.enabled = s.Enabled;
            }

            foreach (ParticleState s in _particles)
            {
                if (s.System == null)
                    continue;

                s.System.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);

                ParticleSystem.MainModule main = s.System.main;
                main.startColor = s.StartColor;
                main.startLifetime = s.StartLifetime;

                ParticleSystem.EmissionModule emission = s.System.emission;
                emission.rateOverDistance = s.RateOverDistance;
            }

            foreach (LightState s in _lights)
            {
                if (s.Light != null)
                    s.Light.LightColor = s.Color;
            }

            // Last, once every pose is back: activeSelf of the children (the root's own is the
            // pool's business). A particle slot ProjectileDataVisualsView switched off for one
            // weapon comes back on for the next one's authored default.
            foreach (TransformState s in _transforms)
            {
                if (s.Transform != null)
                    s.Transform.gameObject.SetActive(s.Active);
            }
        }
    }
}
