namespace QuantumUser.View.Managers
{
    using System.Collections;
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;

    // Growing ring OUTLINE for RingSlamDeliveryData's own expanding damage wave - listens for
    // EventRingWaveExpanding and draws a LineRenderer circle whose radius is read live off
    // Enemy.RingWaveRadius every render frame (not replicated from the growth formula client-side -
    // see that event's own comment for why: Void Pressure scales the real growth, and an
    // independently-clocked replica would drift out of sync with it). Center is fixed at whatever
    // the event carried (RingSlamDeliveryData freezes its own center at Begin() and never re-reads
    // it either), so this never tracks the owning enemy's live position the way
    // RotatingLaserVisualManager's beam does. Stops drawing - and destroys the line - the moment
    // Entity's own Enemy.Phase leaves Active or the entity stops existing, which covers the ring
    // finishing normally, this delivery being interrupted, and the entity dying, all in one check.
    public class RingWaveVisualManager : MonoBehaviour
    {
        [SerializeField, Tooltip("LineRenderer prefab this instantiates per ring - positionCount is overwritten every frame, so only material/color/texture need to be authored on it.")]
        private LineRenderer ringLinePrefab;

        [SerializeField, Tooltip("Points around the circle - higher is smoother but more expensive. 48 reads as a smooth circle at any on-screen size this game's camera actually shows.")]
        private int segments = 48;

        // Same idiom/value as GroundWarningTelegraphManager.GroundSnapRayHeight - the simulation's
        // deterministic ground height doesn't necessarily match the Unity-rendered ground mesh
        // exactly, and under this game's tilted top-down camera even a small Y mismatch reads as a
        // visible XZ pixel offset.
        private const float GroundSnapRayHeight = 20f;
        private static int? _groundLayerMask;

        private static int GroundLayerMask
        {
            get
            {
                _groundLayerMask ??= UnityEngine.LayerMask.GetMask("Ground");
                return _groundLayerMask.Value;
            }
        }

        // Keyed by the owning enemy - lets a fresh RingWaveExpanding for the same entity force-replace
        // whatever ring is already running under its name, rather than relying on that OLD ring's own
        // Phase != Active poll to ever notice it should stop. That poll only samples once per RENDER
        // frame, and Quantum can advance several simulation ticks between two of those - a combo chain
        // (or anything else that re-triggers this attack fast enough) can pass this same enemy through
        // Active -> Recovery -> Preparation -> Telegraph -> Active again entirely inside one such gap,
        // so the OLD ring's coroutine never observes a non-Active sample and just keeps drawing its own
        // stale (frozen-center) line using whatever RingWaveRadius the NEW attack is now writing - the
        // "weird, not fully reset" animation on reuse. Firing this event at all IS the authoritative
        // "a new one just started" signal, so replacing on it sidesteps the frame-skip detection
        // problem entirely instead of trying to catch it after the fact. Same pattern
        // EffectsManager.BeginOverloadChainLine already uses for its own chain lines - tracking the
        // LineRenderer alongside the Coroutine matters because StopCoroutine halts execution
        // immediately at its current yield; RunRing's OWN post-loop cleanup (destroying its line, then
        // clearing its dictionary entry) never runs for a coroutine stopped this way, so this has to
        // destroy the old line itself rather than counting on that.
        private readonly Dictionary<EntityRef, (Coroutine Coroutine, LineRenderer Line)> _activeRings = new Dictionary<EntityRef, (Coroutine, LineRenderer)>();

        private void OnEnable()
        {
            QuantumEvent.Subscribe<EventRingWaveExpanding>(this, OnRingWaveExpanding);
        }

        private void OnDisable()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnRingWaveExpanding(EventRingWaveExpanding e)
        {
            if (ringLinePrefab == null)
                return;

            if (_activeRings.TryGetValue(e.Entity, out var stale))
            {
                StopCoroutine(stale.Coroutine);

                if (stale.Line != null)
                    Destroy(stale.Line.gameObject);

                _activeRings.Remove(e.Entity);
            }

            Vector3 center = SnapToGround(e.Center.ToUnityVector3());

            LineRenderer line = Instantiate(ringLinePrefab, center, Quaternion.identity);
            line.startWidth = e.LineWidth.AsFloat;
            line.endWidth = e.LineWidth.AsFloat;
            line.loop = true;

            Coroutine coroutine = StartCoroutine(RunRing(e.Entity, center, line));
            _activeRings[e.Entity] = (coroutine, line);
        }

        // QuantumRunner.Default, not e.Game - this runs across several Unity frames well after the
        // triggering event has finished dispatching, same live-read idiom
        // EffectsManager.ResolveLiveTargetPosition already uses. Only ever reaches its own tail
        // (below the loop) on NATURAL completion - a coroutine stopped externally via
        // OnRingWaveExpanding's own StopCoroutine call never resumes to run this cleanup at all, so
        // reaching it here already proves nothing has replaced this entry, and removing it
        // unconditionally is safe.
        private IEnumerator RunRing(EntityRef entity, Vector3 center, LineRenderer line)
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

                float radius = ResolveInterpolatedRadius(game, entity, enemy.RingWaveRadius.AsFloat);
                DrawCircle(line, center, radius, segments);

                yield return null;
            }

            _activeRings.Remove(entity);

            if (line != null)
                Destroy(line.gameObject);
        }

        // RingWaveRadius only actually changes once per SIMULATION tick, not once per render frame -
        // reading it raw off Frames.Predicted alone looks stepped/juddery whenever the render frame
        // rate runs ahead of the simulation tick rate (the same reason a raw Transform3D read would
        // look stepped too, if QuantumEntityView didn't already interpolate that one automatically).
        // Blends toward the current tick's value from the PREVIOUS tick's using Game.InterpolationFactor -
        // the exact same two ingredients QuantumEntityView's own position/rotation interpolation uses
        // (see QuantumEntityView.InterpolationAlpha) - so the ring grows exactly as smoothly as
        // everything else being rendered. Falls back to the current value alone if there's no valid
        // previous-tick sample yet (e.g. the very first frame right after this ring spawned).
        private static float ResolveInterpolatedRadius(QuantumGame game, EntityRef entity, float currentRadius)
        {
            Frame previousFrame = game.Frames.PredictedPrevious;

            if (previousFrame == null || previousFrame.Exists(entity) == false)
                return currentRadius;

            float previousRadius = previousFrame.Get<Enemy>(entity).RingWaveRadius.AsFloat;
            return Mathf.Lerp(previousRadius, currentRadius, game.InterpolationFactor);
        }

        private static void DrawCircle(LineRenderer line, Vector3 center, float radius, int segments)
        {
            line.positionCount = segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        // Real UnityEngine.Physics raycast, not Quantum's - purely a view-layer placement fix, same
        // as GroundWarningTelegraphManager/EnemyAttackVisualsView's own SnapToGround. Leaves
        // position.y untouched if nothing on the Ground layer is found beneath/above it.
        private static Vector3 SnapToGround(Vector3 position)
        {
            Vector3 rayOrigin = position + Vector3.up * GroundSnapRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, GroundSnapRayHeight * 2f, GroundLayerMask))
                position.y = hit.point.y;

            return position;
        }
    }
}
