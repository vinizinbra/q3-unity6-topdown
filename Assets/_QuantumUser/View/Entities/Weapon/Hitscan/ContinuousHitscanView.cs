using System.Collections.Generic;
using Photon.Deterministic;
using UnityEngine;

namespace Quantum
{
    // Hitscan style 3: ONE persistent beam, for a weapon that fires every tick (the BeamGun, and any
    // future flamethrower/laser). The other two styles draw a discrete tracer per shot, which on a
    // continuously-firing weapon reads as a stutter of overlapping lines rather than a held beam.
    //
    // Instead of spawning anything per shot, this keeps a single line alive and rewrites its points
    // as the shots arrive, then hides it once they stop (stopGrace - the same "no new shot within
    // this window means the trigger was released" idiom WeaponView.beamStopGrace already uses for
    // its own looping beam particle).
    //
    // A Ricochet bounce becomes extra points on the same polyline rather than a separate beam: each
    // tick's first segment starts at the muzzle and so doesn't chain onto the previous point, which
    // restarts the line; every bounce does chain, and appends. That also means this style assumes a
    // SINGLE-pellet weapon - a continuous multi-pellet volley would have its pellets fighting over
    // one line, and wants LineRendererHitscanView instead.
    public class ContinuousHitscanView : HitscanViewBase
    {
        [Header("Templates - disabled children of this weapon's prefab")]
        [SerializeField, Tooltip("The beam itself, held for as long as the weapon keeps firing. One copy, unparented and driven in world space (useWorldSpace is forced on).")]
        private LineRenderer beamTemplate;
        [SerializeField, Tooltip("Optional LOOPING particle held where the beam currently lands - started when the beam starts hitting something and stopped when it stops. Leave empty to skip.")]
        private ParticleSystem impactTemplate;
        [SerializeField, Tooltip("On: the impact particle only plays while the beam is on an actual enemy (EventHitscanFired.Target). Off: it also plays on level geometry.")]
        private bool impactOnEnemiesOnly;

        [Header("Sound")]
        [SerializeField, Tooltip("Held muzzle loop for as long as the beam is firing - intro/loop/tail, all optional. Started on the first segment, kept alive by every later one, and stopped by the same stopGrace window that hides the beam, so the sound and the visual always end together. A continuous weapon should use THIS rather than WeaponView.fireSound, which would otherwise fire a one-shot every simulated tick.")]
        private SustainedSound fireLoop = new SustainedSound();

        [Header("Scrolling")]
        [SerializeField, Tooltip("Texture repeats per world unit of beam length - the same knob LineRendererHitscanView has, and it matters MORE here: a held beam's length changes continuously as the target moves, so the default Stretch mode visibly squashes and smears the texture along it as the range closes. Tile keeps the pattern (and a scrolling material's flow rate) constant whatever the beam currently reaches. 0 leaves the template's own authored texture mode alone. The texture itself must be set to Repeat rather than Clamp for this to read correctly.")]
        private float textureTilesPerUnit;

        [Header("Timing")]
        [SerializeField, Tooltip("Floor on how long the beam is held after the last shot. Only a floor - the real grace is derived per shot from the weapon's own LIVE fire interval (see ResolveStopGrace), so a weapon whose fire rate changes mid-run, or one that simply isn't fast enough to read as continuous, can't blink the beam off between ticks.")]
        private float stopGrace = 0.15f;
        [SerializeField, Tooltip("How many fire intervals to hold the beam for. Above 1 so a single skipped/late tick doesn't blink it; too high and the beam visibly lingers after the trigger is released.")]
        private float graceIntervals = 2f;

        private readonly List<LineRenderer> beamPool = new List<LineRenderer>();
        private readonly List<ParticleSystem> impactPool = new List<ParticleSystem>();
        private readonly List<Vector3> points = new List<Vector3>();

        private LineRenderer beam;
        private ParticleSystem impact;
        private float lastSegmentTime;
        private float resolvedStopGrace;
        private bool firing;

        public override void Awake()
        {
            base.Awake();

            PrepareTemplate(beamTemplate);
            PrepareTemplate(impactTemplate);
        }

        protected override void OnSegment(in HitscanSegment segment)
        {
            bool chains = firing == true
                && points.Count > 0
                && (points[points.Count - 1] - segment.Origin).sqrMagnitude <= ChainTolerance * ChainTolerance;

            if (chains == false)
            {
                points.Clear();

                // The LIVE muzzle (HitscanViewBase.MuzzlePosition), not segment.Origin - and read
                // here rather than trusting the base's own opening-leg substitution, since a beam is
                // held across frames and RefreshOrigin below has to keep re-reading it either way:
                // segments only arrive once per simulated tick, and a beam pinned to a tick-old
                // position visibly drags behind the gun while the player runs. Unlike the other two
                // styles this also holds when no muzzle is assigned, falling back to this
                // component's own transform. Only point 0 - every later point is a resolved hit
                // position and must stay exactly where the simulation put it.
                points.Add(MuzzlePosition);
            }

            points.Add(segment.EndPoint);

            lastSegmentTime = Time.time;
            resolvedStopGrace = ResolveStopGrace();
            firing = true;

            // Same window the beam is held for, so the sound can never outlive the visual or cut
            // out from under it - one number, derived from the weapon's own live fire interval.
            fireLoop.Keep(MuzzleTransform, resolvedStopGrace, EntitySound.ResolveVolume(fireLoop.Loop, _entityRef));

            ApplyBeam();
            ApplyImpact(segment);
        }

        private void ApplyBeam()
        {
            if (beam == null)
                beam = Acquire(beamTemplate, beamPool);

            if (beam == null)
                return;

            beam.gameObject.SetActive(true);
            beam.useWorldSpace = true;
            beam.positionCount = points.Count;

            // Tile mode repeats per WORLD UNIT, so this is length-independent - it does not need
            // re-deriving as the beam stretches, and RefreshOrigin deliberately doesn't touch it.
            // Set here rather than once on acquire purely because a pooled instance is reused; both
            // writes inside ApplyTextureTiling are guarded against redundant assignment.
            ApplyTextureTiling(beam, textureTilesPerUnit);

            for (int i = 0; i < points.Count; i++)
                beam.SetPosition(i, points[i]);
        }

        private void ApplyImpact(in HitscanSegment segment)
        {
            if (impactTemplate == null)
                return;

            bool wanted = segment.DidHit == true && (impactOnEnemiesOnly == false || segment.HitEnemy == true);

            if (wanted == false)
            {
                StopImpact();
                return;
            }

            if (impact == null)
                impact = Acquire(impactTemplate, impactPool);

            if (impact == null)
                return;

            impact.gameObject.SetActive(true);
            impact.transform.SetPositionAndRotation(segment.EndPoint,
                Quaternion.LookRotation(-segment.Direction, Vector3.up));

            if (impact.isPlaying == false)
                impact.Play(true);
        }

        private void StopImpact()
        {
            if (impact == null || impact.isPlaying == false)
                return;

            impact.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected override void QUpdate(QuantumGame game)
        {
            fireLoop.Tick(Time.deltaTime);

            if (firing == false)
                return;

            // A reload ends the burst outright - shots simply stop arriving, so the grace window
            // alone would leave the muzzle loop droning on into the reload. Read straight off the
            // predicted frame; ResolveStopGrace already fetches this component, so it's free.
            if (IsReloading(game))
            {
                fireLoop.Stop();
                StopBeam();
                return;
            }

            if (Time.time - lastSegmentTime < resolvedStopGrace)
            {
                // Every frame, not just on a shot: the gun moves with the player, the aim, and its
                // own sway/recoil, while segments only arrive once per simulated tick.
                RefreshOrigin();
                return;
            }

            fireLoop.Stop();
            StopBeam();
        }

        private bool IsReloading(QuantumGame game)
        {
            Frame frame = game != null ? game.Frames.Predicted : null;

            return frame != null
                && frame.TryGet<Weapon>(_entityRef, out var weapon)
                && weapon.ReloadTimer > FP._0;
        }

        private void StopBeam()
        {
            firing = false;
            points.Clear();

            StopImpact();

            // Deactivated rather than left with 0 points: an inactive instance is exactly what
            // HitscanViewBase.Acquire reads as "free", so this is also what returns the beam to the
            // pool for the next burst.
            if (beam != null)
            {
                beam.gameObject.SetActive(false);
                beam = null;
            }

            if (impact != null)
            {
                impact.gameObject.SetActive(false);
                impact = null;
            }
        }

        private void RefreshOrigin()
        {
            if (beam == null || points.Count == 0)
                return;

            points[0] = MuzzlePosition;
            beam.SetPosition(0, points[0]);
        }

        // "Continuous" is not a property of the WEAPON - mechanically it is just a hitscan weapon
        // with a short enough fire interval, and the simulation neither knows nor needs to. What
        // makes a weapon continuous is that its view prefab carries THIS component instead of one of
        // the other two styles. The only thing that ever needed to agree with the weapon's own
        // tuning is this grace window, so it is read from the weapon rather than authored twice:
        // Weapon.FireCooldownTimer is set to the full live interval the instant a shot fires (see
        // WeaponSystem.ResolveLiveFireCooldown), with every multiplier, perk and Haste effect already
        // folded in, so a Fire Rate perk picked mid-run widens this by itself.
        private float ResolveStopGrace()
        {
            float interval = 0f;

            Frame frame = _game != null ? _game.Frames.Predicted : null;

            if (frame != null && frame.TryGet<Weapon>(_entityRef, out var weapon) == true)
                interval = weapon.FireCooldownTimer.AsFloat;

            return Mathf.Max(stopGrace, interval * graceIntervals);
        }

        // Both instances have just gone back in the pool (the weapon was switched off mid-burst -
        // see HitscanViewBase.OnDisable), so this beam has nothing left to hold.
        protected override void OnInstancesReturned()
        {
            // playTail: false - the weapon is gone, so a spin-down trailing after it is wrong.
            fireLoop.Stop(false);

            beam = null;
            impact = null;
            points.Clear();
            firing = false;
        }
    }
}
