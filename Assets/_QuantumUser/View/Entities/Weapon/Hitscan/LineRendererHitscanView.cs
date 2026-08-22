using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace Quantum
{
    // Hitscan style 1: an instant line drawn along every segment of the shot, fading out where it
    // was drawn. The classic tracer - reads well on a fast, punchy weapon (a sniper, a pistol), and
    // handles a Ricochet bounce for free since each bounce arrives as its own segment and simply
    // draws its own line.
    //
    // Everything it spawns comes from disabled child templates of this weapon's prefab, instantiated
    // unparented and reused by activity - see HitscanViewBase for why templates live in the
    // hierarchy rather than as project prefabs.
    public class LineRendererHitscanView : HitscanViewBase
    {
        [Header("Templates - disabled children of this weapon's prefab")]
        [SerializeField, Tooltip("The tracer line itself. Copies are unparented and driven in world space (useWorldSpace is forced on), so this template's own transform doesn't matter - only its material, width curve and colors do.")]
        private LineRenderer lineTemplate;
        [SerializeField, Tooltip("Optional one-shot particle played at the muzzle end of every segment. The muzzle FLASH belongs on WeaponView.muzzleParticle instead - this is for something that should sit at the start of the tracer itself. Leave empty to skip.")]
        private ParticleSystem muzzleParticleTemplate;
        [SerializeField, Tooltip("Optional one-shot particle played where the segment landed, oriented back along the shot. Leave empty to skip.")]
        private ParticleSystem hitParticleTemplate;
        [SerializeField, Tooltip("On: the hit particle only plays on an actual enemy (EventHitscanFired.Target). Off: it also plays on level geometry, which is what you want for sparks/dust.")]
        private bool hitParticleOnEnemiesOnly;

        [Header("Scrolling")]
        [SerializeField, Tooltip("Texture repeats per world unit of shot length, for a line whose material scrolls its own UVs (Project/Scrolling Beam). Only meaningful with such a material: the scroll is driven by _Time in the shader, so nothing here does per-frame work or touches a material property - this only sets the line's texture mode to Tile so the flow rate stays constant whatever the shot's length. 0 leaves the template's own authored texture mode alone, which is what a plain solid-color tracer wants.")]
        private float textureTilesPerUnit;

        [Header("Timing")]
        [SerializeField, Tooltip("Seconds the line takes to fade from its authored colors to fully transparent, after which it goes back in the pool. Overlapping fades from a fast weapon are what make a rapid volley read as one near-continuous beam rather than discrete flashes.")]
        private float fadeDuration = 0.05f;

        private readonly List<LineRenderer> linePool = new List<LineRenderer>();
        private readonly List<ParticleSystem> muzzlePool = new List<ParticleSystem>();
        private readonly List<ParticleSystem> hitPool = new List<ParticleSystem>();

        // Captured off the TEMPLATE, never off a live instance - an instance's own colors are
        // already faded to transparent from its previous shot, so reading them back would leave every
        // reuse invisible.
        private Color baseStartColor = Color.white;
        private Color baseEndColor = Color.white;

        public override void Awake()
        {
            base.Awake();

            PrepareTemplate(lineTemplate);
            PrepareTemplate(muzzleParticleTemplate);
            PrepareTemplate(hitParticleTemplate);

            if (lineTemplate != null)
            {
                baseStartColor = lineTemplate.startColor;
                baseEndColor = lineTemplate.endColor;
            }
        }

        protected override void OnSegment(in HitscanSegment segment)
        {
            DrawLine(segment);

            if (muzzleParticleTemplate != null)
                PlayOneShot(AcquireOneShot(muzzleParticleTemplate, muzzlePool), segment.Origin, segment.Direction);

            if (ShouldPlayHitParticle(segment) == true)
                PlayOneShot(AcquireOneShot(hitParticleTemplate, hitPool), segment.EndPoint, -segment.Direction);
        }

        private bool ShouldPlayHitParticle(in HitscanSegment segment)
        {
            if (hitParticleTemplate == null || segment.DidHit == false)
                return false;

            return hitParticleOnEnemiesOnly == false || segment.HitEnemy == true;
        }

        private void DrawLine(in HitscanSegment segment)
        {
            LineRenderer line = Acquire(lineTemplate, linePool);

            if (line == null)
                return;

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, segment.Origin);
            line.SetPosition(1, segment.EndPoint);
            line.startColor = baseStartColor;
            line.endColor = baseEndColor;

            ApplyTextureTiling(line, textureTilesPerUnit);

            // A previous fade on this same instance is stopped first - a reused line would otherwise
            // have two tweens racing to set its colors, and the older one wins the moment it
            // completes, blanking a tracer that was just fired.
            Tween.StopAll(line);

            Tween.Custom(line, 1f, 0f, fadeDuration, (target, value) =>
            {
                if (target == null)
                    return;

                target.startColor = WithAlpha(baseStartColor, value);
                target.endColor = WithAlpha(baseEndColor, value);
            }).OnComplete(() =>
            {
                if (line != null)
                    line.gameObject.SetActive(false);
            });
        }

        // A one-shot particle returns itself to the pool once it has finished playing - derived from
        // the system's own duration/lifetime rather than a hand-tuned field, so retuning the effect
        // can't leave a stale number next to it.
        private ParticleSystem AcquireOneShot(ParticleSystem template, List<ParticleSystem> pool)
        {
            ParticleSystem instance = Acquire(template, pool);

            if (instance == null)
                return null;

            Tween.StopAll(instance.gameObject);
            Tween.Delay(instance.gameObject, ResolveParticleLifetime(template), () =>
            {
                if (instance != null)
                    instance.gameObject.SetActive(false);
            });

            return instance;
        }

        private static Color WithAlpha(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}
