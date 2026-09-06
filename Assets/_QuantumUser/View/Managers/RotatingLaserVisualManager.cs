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
    // RotatingLaserDeliveryData.FireLaserTick). Stops drawing - and destroys every beam - the moment
    // Entity's own Enemy.Phase leaves Active or the entity stops existing, which covers the spin
    // finishing normally, this delivery being interrupted, and the entity dying, all in one check.
    public class RotatingLaserVisualManager : MonoBehaviour
    {
        [SerializeField, Tooltip("LineRenderer prefab this instantiates per beam - positionCount/width are overwritten every frame, so only material/color/texture need to be authored on it.")]
        private LineRenderer laserLinePrefab;

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
            if (laserLinePrefab == null)
                return;

            if (_activeLasers.TryGetValue(e.Entity, out var stale))
            {
                StopCoroutine(stale.Coroutine);

                foreach (LineRenderer staleLine in stale.Lines)
                {
                    if (staleLine != null)
                        Destroy(staleLine.gameObject);
                }

                _activeLasers.Remove(e.Entity);
            }

            int beamCount = Mathf.Max(1, (int)e.BeamCount);
            var lines = new List<LineRenderer>(beamCount);

            for (int i = 0; i < beamCount; i++)
            {
                LineRenderer line = Instantiate(laserLinePrefab);
                line.positionCount = 2;
                line.startWidth = e.Width.AsFloat;
                line.endWidth = e.Width.AsFloat;
                lines.Add(line);
            }

            float length = e.Length.AsFloat;
            float heightOffset = e.HeightOffset.AsFloat;

            // Snapped synchronously, right here, rather than leaving the very first placement to
            // RunLaser's own loop - Instantiate above creates each line with no position at all
            // (unlike RingWaveVisualManager, which instantiates directly at its ring's center), so
            // without this every beam would render at wherever the prefab's own transform happens to
            // sit (typically the origin) for however long it takes the coroutine to reach its first
            // SetPosition call. e.Game.Frames.Predicted is safe to read here (unlike inside the
            // coroutine, which runs long after this event has finished dispatching and needs
            // QuantumRunner.Default instead).
            Frame frame = e.Game.Frames.Predicted;

            if (frame != null && frame.Exists(e.Entity) == true)
            {
                Vector3 position = frame.Get<Transform3D>(e.Entity).Position.ToUnityVector3();
                float angle = frame.Get<Enemy>(e.Entity).LaserSpinAngle.AsFloat;
                UpdateLaserLines(position + Vector3.up * (heightOffset + visualLift), angle, length, lines);
            }

            Coroutine coroutine = StartCoroutine(RunLaser(e.Entity, length, heightOffset, lines));
            _activeLasers[e.Entity] = (coroutine, lines);
        }

        // QuantumRunner.Default, not e.Game - this runs across several Unity frames well after the
        // triggering event has finished dispatching, same live-read idiom
        // EffectsManager.ResolveLiveTargetPosition already uses. Only ever reaches its own tail (below
        // the loop) on NATURAL completion - a coroutine stopped externally via OnRotatingLaserFired's
        // own StopCoroutine call never resumes to run this cleanup at all, so reaching it here already
        // proves nothing has replaced this entry, and removing it unconditionally is safe.
        private IEnumerator RunLaser(EntityRef entity, float length, float heightOffset, List<LineRenderer> lines)
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
                ResolveInterpolated(game, entity, ref position, ref angle);

                Vector3 origin = position + Vector3.up * (heightOffset + visualLift);
                UpdateLaserLines(origin, angle, length, lines);

                yield return null;
            }

            _activeLasers.Remove(entity);

            foreach (LineRenderer line in lines)
            {
                if (line != null)
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
        // there's no wraparound discontinuity to worry about. Leaves both untouched (the current-tick
        // values already assigned by the caller) if there's no valid previous-tick sample yet (e.g.
        // the very first frame right after this laser spawned).
        private static void ResolveInterpolated(QuantumGame game, EntityRef entity, ref Vector3 position, ref float angle)
        {
            Frame previousFrame = game.Frames.PredictedPrevious;

            if (previousFrame == null || previousFrame.Exists(entity) == false)
                return;

            Vector3 previousPosition = previousFrame.Get<Transform3D>(entity).Position.ToUnityVector3();
            float previousAngle = previousFrame.Get<Enemy>(entity).LaserSpinAngle.AsFloat;

            position = Vector3.Lerp(previousPosition, position, game.InterpolationFactor);
            angle = Mathf.Lerp(previousAngle, angle, game.InterpolationFactor);
        }
    }
}
