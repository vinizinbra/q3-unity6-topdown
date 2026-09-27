namespace QuantumUser.View.Managers
{
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;

    // Generic ground-landing-warning telegraph, decoupled from any specific enemy entity - listens
    // for EventProjectileLandingWarning (Position/Duration/Radius, plus an optional Owner) and pulls an instance
    // straight from TelegraphManager's pool, exactly the way EnemyAttackVisualsView does for a
    // caster's own windup telegraph (same TelegraphFade/TelegraphGrow prefab shape) - the only
    // difference is there's no owning enemy and no single-slot bookkeeping, since TelegraphManager's
    // pool already supports any number of simultaneous independent Get()/Release() instances on its
    // own. Mortar's barrage is the first consumer, but nothing here is projectile-specific - the
    // event only carries a point/duration/radius, so any future "several things are about to happen
    // at ground points, with a fair warning first" attack (a boss dropping a volley of spikes, an
    // AoE barrage with no real projectile at all) can fire the exact same event directly
    // (f.Events.ProjectileLandingWarning(point, authoredFuseTime, radius, owner) - no flight-time math
    // needed when the duration is just an authored fuse) instead of building its own marker/telegraph
    // plumbing from scratch.
    //
    // Owner-bound warnings (EventProjectileLandingWarning.Owner != None - GroundBarrageDeliveryData,
    // whose caster's own Active Tick is what detonates the point) are also dropped early the moment
    // that owner can no longer detonate on schedule: stunned/frozen, interrupted out of Active, dead
    // or gone. Ownerless ones (a real lobbed shell already in flight) always run their full Duration.
    public class GroundWarningTelegraphManager : MonoBehaviour
    {
        private struct ActiveWarning
        {
            public TelegraphFade Fade;
            public EntityRef Owner;
            public float FadeOutTime;
        }

        [SerializeField, Tooltip("Prefab pulled from TelegraphManager's pool - must carry a TelegraphFade on its root (same shape as any other TelegraphPrefab), optionally with a child TelegraphGrow for a fill-in animation.")]
        private GameObject warningTelegraphPrefab;

        [SerializeField] private float fadeInDuration = 0.15f;
        [SerializeField] private float fadeOutDuration = 0.15f;

        // How far above/below the simulation's own landing position to search for real Unity
        // ground - same idiom/value as EnemyAttackVisualsView.GroundSnapRayHeight. The simulation's
        // deterministic idea of ground height doesn't necessarily match the Unity-rendered ground
        // mesh exactly, and under this game's tilted top-down camera even a small Y mismatch
        // projects onto screen as a visible XZ pixel offset - which is exactly what an unsnapped
        // marker looked like ("landing center is a few pixels off").
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

        // Tracked per instance rather than a fire-and-forget coroutine - an owner-bound warning can be
        // faded out early (see UpdateOwnerBound), and a coroutine still holding that pooled
        // TelegraphFade would later fade out whatever unrelated warning reused the instance.
        private readonly List<ActiveWarning> _active = new List<ActiveWarning>();

        private void OnEnable()
        {
            QuantumEvent.Subscribe<EventProjectileLandingWarning>(this, OnProjectileLandingWarning);
        }

        private void OnDisable()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnProjectileLandingWarning(EventProjectileLandingWarning e)
        {
            if (warningTelegraphPrefab == null)
                return;

            Vector3 position = SnapToGround(e.Position.ToUnityVector3());
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward); // flat ground decal, same convention Circle telegraphs use

            GameObject instance = TelegraphManager.Instance != null
                ? TelegraphManager.Instance.Get(warningTelegraphPrefab, position, rotation)
                : Instantiate(warningTelegraphPrefab, position, rotation);

            float radius = e.Radius.AsFloat;
            instance.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            float duration = e.Duration.AsFloat;

            TelegraphFade fade = instance.GetComponent<TelegraphFade>();

            if (fade == null)
            {
                // No TelegraphFade authored on this prefab - nothing owns releasing it back to the
                // pool, so just destroy it directly after its duration instead of leaking it.
                Object.Destroy(instance, duration);
                return;
            }

            fade.Initialize(warningTelegraphPrefab, fadeInDuration, fadeOutDuration, duration, EntityRef.None);

            _active.Add(new ActiveWarning
            {
                Fade = fade,
                Owner = e.Owner,
                FadeOutTime = Time.time + Mathf.Max(duration - fadeOutDuration, 0f),
            });
        }

        private void Update()
        {
            if (_active.Count == 0)
                return;

            QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
            Frame frame = game?.Frames.Predicted;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveWarning warning = _active[i];

                bool expired = Time.time >= warning.FadeOutTime;
                bool ownerCancelled = warning.Owner != EntityRef.None && frame != null && CanOwnerStillDetonate(frame, warning.Owner) == false;

                if (expired == false && ownerCancelled == false)
                    continue;

                if (warning.Fade != null)
                    warning.Fade.FadeOutAndRelease();

                _active.RemoveAt(i);
            }
        }

        // The owner's own Active Tick is the only thing that detonates an owner-bound point - any of
        // these means that detonation isn't coming on schedule anymore (or at all).
        private static bool CanOwnerStillDetonate(Frame frame, EntityRef owner)
        {
            if (frame.TryGet(owner, out Enemy enemy) == false)
                return false;

            if (enemy.Phase != EnemyActionPhase.Active)
                return false;

            return StatusEffectUtility.IsStunned(frame, owner) == false && StatusEffectUtility.IsFrozen(frame, owner) == false;
        }

        // Real UnityEngine.Physics raycast, not Quantum's - purely a view-layer placement fix, same
        // as EnemyAttackVisualsView's own SnapToGround. Leaves position.y untouched if nothing on
        // the Ground layer is found beneath/above it.
        private static Vector3 SnapToGround(Vector3 position)
        {
            Vector3 rayOrigin = position + Vector3.up * GroundSnapRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, GroundSnapRayHeight * 2f, GroundLayerMask))
                position.y = hit.point.y;

            return position;
        }
    }
}
