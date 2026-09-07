namespace QuantumUser.View.Managers
{
    using System.Collections;
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;

    // Persistent spinning beam(s) for RotatingLaserDeliveryData - listens for EventRotatingLaserFired
    // and draws BeamCount 2-point LineRenderers from the owning enemy's own live position (tracked
    // every render frame, since - unlike RingWaveVisualManager's frozen ring center - the sim's own
    // FireLaserTick re-reads Transform3D.Position fresh every tick too), evenly spaced around
    // Enemy.LaserSpinAngle. The angle is read live off the frame rather than replicated from the
    // spin-speed formula client-side - see that event's own comment for why: Void Pressure scales
    // the real spin rate, and an independently-clocked replica would drift out of sync with it.
    // HeightOffset is likewise carried from the event rather than authored separately here, so the
    // visual can never sit at a different height than the real hit-box (see
    // RotatingLaserDeliveryData.FireLaserTick). The prefab itself is authored per-delivery-asset
    // (RotatingLaserDeliveryData.LineRendererPrefab, resolved off the event's own Delivery AssetRef)
    // rather than a single shared field here, so different lasers can look different. Stops drawing -
    // and destroys every beam - the moment Entity's own Enemy.Phase leaves Active or the entity stops
    // existing, which covers the spin finishing normally, this delivery being interrupted, and the
    // entity dying, all in one check.
    public class RotatingLaserVisualManager : MonoBehaviour
    {
        // Purely cosmetic anti-Z-fighting nudge on top of the real (gameplay) HeightOffset the event
        // carries - not a second gameplay-relevant height knob, just enough to keep the line from
        // fighting a flat ground mesh sitting exactly at the beam's own height.
        [SerializeField] private float visualLift = 0.05f;

        // Keyed by the owning enemy - lets a fresh RotatingLaserFired for the same entity force-replace
        // whatever beams are already spinning under its name, rather than relying on that OLD spin's
        // own Phase != Active poll to ever notice it should stop - same reasoning/fix as
        // RingWaveVisualManager's own identical dictionary (see its comment for the full "combo chain
        // skips the reset window between two render frames" explanation). StopCoroutine halts execution
        // immediately at its current yield, so RunLaser's own post-loop cleanup never runs for a
        // coroutine stopped this way - this has to destroy the old lines itself rather than counting on
        // that, same as RingWaveVisualManager.
        private readonly Dictionary<EntityRef, (Coroutine Coroutine, List<LineRenderer> Lines)> _activeLasers = new Dictionary<EntityRef, (Coroutine, List<LineRenderer>)>();

        private void OnEnable()
        {
            QuantumEvent.Subscribe<EventRotatingLaserFired>(this, OnRotatingLaserFired);
        }

        private void OnDisable()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnRotatingLaserFired(EventRotatingLaserFired e)
        {
            // Snapped synchronously, right here, rather than leaving the very first placement to
            // RunLaser's own loop - Instantiate below creates each line with no position at all
            // (unlike RingWaveVisualManager, which instantiates directly at its ring's center), so
            // without this every beam would render at wherever the prefab's own transform happens to
            // sit (typically the origin) for however long it takes the coroutine to reach its first
            // SetPosition call. e.Game.Frames.Predicted is safe to read here (unlike inside the
            // coroutine, which runs long after this event has finished dispatching and needs
            // QuantumRunner.Default instead).
            Frame frame = e.Game.Frames.Predicted;

            // Authored per-delivery-asset (RotatingLaserDeliveryData.LineRendererPrefab, its own
            // View.cs partial) rather than a single shared prefab on this manager, so different
            // lasers can use different visuals - same "AssetRef travels with the event, View
            // resolves + pattern matches" idiom EnemyDeliveryData.ResolveWarningRadius already uses
            // for AreaHitData.
            LineRenderer prefab = frame != null && frame.FindAsset(e.Delivery) is RotatingLaserDeliveryData laserDelivery
                ? laserDelivery.LineRendererPrefab
                : null;

            if (prefab == null)
                return;

            if (_activeLasers.TryGetValue(e.Entity, out var stale))
            {
                StopCoroutine(stale.Coroutine);
                ResetLines(stale.Lines);
                _activeLasers.Remove(e.Entity);
            }

            int beamCount = Mathf.Max(1, (int)e.BeamCount);
            var lines = new List<LineRenderer>(beamCount);

            for (int i = 0; i < beamCount; i++)
            {
                // Width is deliberately NOT set here - whatever startWidth/endWidth (or width curve)
                // the prefab itself was authored with applies unmodified. BeamWidth on the delivery
                // is real-hitbox-only now, never carried to this event at all (see
                // RotatingLaserDeliveryData.BeamWidth's own comment).
                LineRenderer line = Instantiate(prefab);
                line.positionCount = 2;

                // Stays off until UpdateLaserLines actually writes real endpoints onto it - never
                // shows even a single frame of whatever the prefab happened to be authored/left at.
                line.enabled = false;

                lines.Add(line);
            }

            float length = e.Length.AsFloat;
            float heightOffset = e.HeightOffset.AsFloat;

            if (frame != null && frame.Exists(e.Entity) == true)
            {
                Vector3 position = frame.Get<Transform3D>(e.Entity).Position.ToUnityVector3();
                float angle = frame.Get<Enemy>(e.Entity).LaserSpinAngle.AsFloat;
                UpdateLaserLines(position + Vector3.up * (heightOffset + visualLift), angle, length, lines);
            }

            // The tick this laser's own LaserSpinAngle was actually (re)written on - see RunLaser's
            // own comment on why this, not just "the coroutine's first loop iteration", is the correct
            // guard against interpolating across a reused enemy's stale leftover value.
            int spawnTick = frame != null ? frame.Number : -1;

            Coroutine coroutine = StartCoroutine(RunLaser(e.Entity, length, heightOffset, lines, spawnTick));
            _activeLasers[e.Entity] = (coroutine, lines);
        }

        // QuantumRunner.Default, not e.Game - this runs across several Unity frames well after the
        // triggering event has finished dispatching, same live-read idiom
        // EffectsManager.ResolveLiveTargetPosition already uses. Only ever reaches its own tail (below
        // the loop) on NATURAL completion - a coroutine stopped externally via OnRotatingLaserFired's
        // own StopCoroutine call never resumes to run this cleanup at all, so reaching it here already
        // proves nothing has replaced this entry, and removing it unconditionally is safe.
        private IEnumerator RunLaser(EntityRef entity, float length, float heightOffset, List<LineRenderer> lines, int spawnTick)
        {
            while (true)
            {
                QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
                Frame frame = game?.Frames.Predicted;

                if (frame == null || frame.Exists(entity) == false)
                    break;

                Enemy enemy = frame.Get<Enemy>(entity);

                if (enemy.Phase != EnemyActionPhase.Active)
                    break;

                Vector3 position = frame.Get<Transform3D>(entity).Position.ToUnityVector3();
                float angle = enemy.LaserSpinAngle.AsFloat;
                ResolveInterpolated(game, entity, spawnTick, ref position, ref angle);

                Vector3 origin = position + Vector3.up * (heightOffset + visualLift);
                UpdateLaserLines(origin, angle, length, lines);

                yield return null;
            }

            _activeLasers.Remove(entity);
            ResetLines(lines);
        }

        // Explicit disable + zero-out rather than counting on Destroy alone - Destroy is deferred to
        // the end of the frame, so without this each line would still be sitting there, enabled, with
        // its last-drawn points, for whatever's left of the current frame's rendering.
        private static void ResetLines(List<LineRenderer> lines)
        {
            foreach (LineRenderer line in lines)
            {
                if (line == null)
                    continue;

                line.enabled = false;
                line.positionCount = 0;
                Destroy(line.gameObject);
            }
        }

        // Shared by RunLaser's own per-frame update and OnRotatingLaserFired's synchronous first-frame
        // snap (see that method's own comment on why the snap can't just wait for the coroutine).
        private static void UpdateLaserLines(Vector3 origin, float angle, float length, List<LineRenderer> lines)
        {
            float beamStep = 360f / lines.Count;

            for (int i = 0; i < lines.Count; i++)
            {
                LineRenderer line = lines[i];

                if (line == null)
                    continue;

                float angleRad = (angle + beamStep * i) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));

                line.SetPosition(0, origin);
                line.SetPosition(1, origin + direction * length);
                line.enabled = true;
            }
        }

        // Position/LaserSpinAngle only actually change once per SIMULATION tick, not once per render
        // frame - reading them raw off Frames.Predicted alone looks stepped/juddery whenever the
        // render frame rate runs ahead of the simulation tick rate (the same reason a raw Transform3D
        // read would look stepped too, if QuantumEntityView didn't already interpolate that one
        // automatically for its own entities). Blends toward the current tick's values from the
        // PREVIOUS tick's using Game.InterpolationFactor - the exact same two ingredients
        // QuantumEntityView's own position/rotation interpolation uses (see
        // QuantumEntityView.InterpolationAlpha) - so the beam(s) move/spin exactly as smoothly as
        // everything else being rendered. A plain Lerp is correct for the angle too, not LerpAngle -
        // LaserSpinAngle is deliberately never wrapped to [0,360) (see Enemy.qtn's own comment), so
        // there's no wraparound discontinuity to worry about within a single spin. Leaves both
        // untouched (the current-tick values already assigned by the caller) unless a genuine
        // post-spawn previous tick exists to blend from.
        //
        // Guarded by spawnTick (the tick THIS laser's own LaserSpinAngle was first written on,
        // captured once in OnRotatingLaserFired), not just "is this the coroutine's first loop
        // iteration" - that weaker guard was tried first and still glitched, because render frame rate
        // can outpace the simulation's own tick rate: if the sim hasn't ticked again yet by the
        // coroutine's SECOND (or Nth) iteration, PredictedPrevious.Number is still < spawnTick, meaning
        // it's STILL a stale sample from whatever this same shared Enemy field last held - a
        // completely different, earlier spin this same enemy fired (e.g. several hundred degrees
        // around), not a real "previous tick of THIS spin" at all.
        private static void ResolveInterpolated(QuantumGame game, EntityRef entity, int spawnTick, ref Vector3 position, ref float angle)
        {
            Frame previousFrame = game.Frames.PredictedPrevious;

            if (previousFrame == null || previousFrame.Exists(entity) == false || previousFrame.Number < spawnTick)
                return;

            Vector3 previousPosition = previousFrame.Get<Transform3D>(entity).Position.ToUnityVector3();
            float previousAngle = previousFrame.Get<Enemy>(entity).LaserSpinAngle.AsFloat;

            position = Vector3.Lerp(previousPosition, position, game.InterpolationFactor);
            angle = Mathf.Lerp(previousAngle, angle, game.InterpolationFactor);
        }
    }
}
