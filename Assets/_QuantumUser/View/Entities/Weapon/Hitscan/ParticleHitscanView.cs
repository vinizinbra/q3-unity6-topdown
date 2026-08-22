using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace Quantum
{
    // Hitscan style 2: a particle that travels the shot's path very fast, rather than a line drawn
    // all at once. On a Ricochet bounce it flies the whole path in order - muzzle to first hit, first
    // hit to second, and so on - because the simulation raises one segment event per leg and they all
    // arrive, in order, in the same frame the shot happened (see HitscanViewBase). So the whole route
    // is known before the particle sets off; only the drawing of it is spread over time.
    //
    // A shot is still instantaneous as far as the simulation is concerned - damage landed on the tick
    // it was fired. This is deliberately a lie the view tells for readability, which is why the travel
    // speed wants to stay high: the slower it is, the further the impact effect drifts from the moment
    // the enemy actually took the damage.
    public class ParticleHitscanView : HitscanViewBase
    {
        [Header("Templates - disabled children of this weapon's prefab")]
        [SerializeField, Tooltip("The travelling projectile-like particle. Copies are unparented and driven in world space; a looping/trailing system reads best here since it is moved rather than replayed per segment.")]
        private ParticleSystem travelTemplate;
        [SerializeField, Tooltip("Optional one-shot particle played wherever a segment lands, oriented back along the shot. Leave empty to skip.")]
        private ParticleSystem hitParticleTemplate;
        [SerializeField, Tooltip("On: the hit particle only plays on an actual enemy (EventHitscanFired.Target). Off: it also plays on level geometry.")]
        private bool hitParticleOnEnemiesOnly;

        [Header("Travel")]
        [SerializeField, Tooltip("World units per second. High on purpose - see the class comment on why a slow travel drifts the impact away from when the damage really landed.")]
        private float travelSpeed = 80f;
        [SerializeField, Tooltip("Hard cap on how long ONE segment may take regardless of travelSpeed - a long-range shot speeds up rather than crawling. 0 disables the cap.")]
        private float maxSegmentDuration = 0.15f;

        private readonly List<ParticleSystem> travelPool = new List<ParticleSystem>();
        private readonly List<ParticleSystem> hitPool = new List<ParticleSystem>();
        private readonly List<Traveler> travelers = new List<Traveler>();

        // One in-flight particle and the path it still has to fly. Pooled alongside its own instance
        // rather than allocated per shot - a fast weapon fires several per second.
        private class Traveler
        {
            public ParticleSystem Instance;
            public readonly List<HitscanSegment> Path = new List<HitscanSegment>();
            public int Index;
            public float Travelled;
            public bool Active;
            public Vector3 PathEnd;
        }

        public override void Awake()
        {
            base.Awake();

            PrepareTemplate(travelTemplate);
            PrepareTemplate(hitParticleTemplate);
        }

        protected override void OnSegment(in HitscanSegment segment)
        {
            // A bounce continues the path of whichever traveller currently ENDS where this segment
            // begins. Comparing against the end of the queued path (not the particle's live position)
            // is what makes this work when every leg of a bounced shot arrives in the same frame,
            // long before the particle has flown any of it.
            for (int i = 0; i < travelers.Count; i++)
            {
                Traveler traveler = travelers[i];

                if (traveler.Active == false)
                    continue;

                if ((traveler.PathEnd - segment.Origin).sqrMagnitude > ChainTolerance * ChainTolerance)
                    continue;

                traveler.Path.Add(segment);
                traveler.PathEnd = segment.EndPoint;
                return;
            }

            StartTraveler(segment);
        }

        private void StartTraveler(in HitscanSegment segment)
        {
            ParticleSystem instance = Acquire(travelTemplate, travelPool);

            if (instance == null)
                return;

            Traveler traveler = GetIdleTraveler();
            traveler.Instance = instance;
            traveler.Path.Clear();
            traveler.Path.Add(segment);
            traveler.Index = 0;
            traveler.Travelled = 0f;
            traveler.PathEnd = segment.EndPoint;
            traveler.Active = true;

            Tween.StopAll(instance.gameObject);
            instance.transform.SetPositionAndRotation(segment.Origin, Quaternion.LookRotation(segment.Direction, Vector3.up));
            instance.Clear(true);
            instance.Play(true);
        }

        private Traveler GetIdleTraveler()
        {
            for (int i = 0; i < travelers.Count; i++)
            {
                if (travelers[i].Active == false)
                    return travelers[i];
            }

            Traveler traveler = new Traveler();
            travelers.Add(traveler);
            return traveler;
        }

        protected override void QUpdate(QuantumGame game)
        {
            for (int i = 0; i < travelers.Count; i++)
            {
                if (travelers[i].Active == true)
                    Advance(travelers[i], Time.deltaTime);
            }
        }

        // Time-based rather than distance-based so leftover time carries into the next leg of a
        // bounce at that leg's own speed - a short first segment shouldn't stall the particle for a
        // frame at the corner.
        private void Advance(Traveler traveler, float deltaTime)
        {
            if (traveler.Instance == null)
            {
                traveler.Active = false;
                return;
            }

            while (deltaTime > 0f && traveler.Index < traveler.Path.Count)
            {
                HitscanSegment segment = traveler.Path[traveler.Index];
                float length = segment.Length;
                float speed = ResolveSpeed(length);
                float remaining = length - traveler.Travelled;

                if (speed * deltaTime < remaining)
                {
                    traveler.Travelled += speed * deltaTime;
                    traveler.Instance.transform.SetPositionAndRotation(
                        segment.Origin + segment.Direction * traveler.Travelled,
                        Quaternion.LookRotation(segment.Direction, Vector3.up));
                    return;
                }

                deltaTime -= remaining / speed;
                traveler.Travelled = 0f;
                traveler.Index++;
                traveler.Instance.transform.position = segment.EndPoint;

                PlayHitParticle(segment);
            }

            if (traveler.Index >= traveler.Path.Count)
                Finish(traveler);
        }

        private float ResolveSpeed(float length)
        {
            float speed = travelSpeed;

            if (maxSegmentDuration > 0f && length > 0f)
                speed = Mathf.Max(speed, length / maxSegmentDuration);

            return Mathf.Max(speed, 0.01f);
        }

        private void PlayHitParticle(in HitscanSegment segment)
        {
            if (hitParticleTemplate == null || segment.DidHit == false)
                return;

            if (hitParticleOnEnemiesOnly == true && segment.HitEnemy == false)
                return;

            ParticleSystem instance = Acquire(hitParticleTemplate, hitPool);

            if (instance == null)
                return;

            PlayOneShot(instance, segment.EndPoint, -segment.Direction);

            Tween.StopAll(instance.gameObject);
            Tween.Delay(instance.gameObject, ResolveParticleLifetime(hitParticleTemplate), () =>
            {
                if (instance != null)
                    instance.gameObject.SetActive(false);
            });
        }

        // Stops emitting rather than deactivating on the spot, then pools the instance once whatever
        // is already in the air has died out - otherwise a trail is cut off mid-flight exactly where
        // it should be finishing.
        private void Finish(Traveler traveler)
        {
            traveler.Active = false;
            traveler.Path.Clear();

            ParticleSystem instance = traveler.Instance;
            traveler.Instance = null;

            if (instance == null)
                return;

            instance.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            Tween.Delay(instance.gameObject, ResolveParticleLifetime(travelTemplate), () =>
            {
                if (instance != null)
                    instance.gameObject.SetActive(false);
            });
        }

        // Every instance has just gone back in the pool (the weapon was switched off mid-flight -
        // see HitscanViewBase.OnDisable), so no traveller still owns one or has a path left to fly.
        protected override void OnInstancesReturned()
        {
            for (int i = 0; i < travelers.Count; i++)
            {
                travelers[i].Active = false;
                travelers[i].Instance = null;
                travelers[i].Path.Clear();
            }
        }
    }
}
