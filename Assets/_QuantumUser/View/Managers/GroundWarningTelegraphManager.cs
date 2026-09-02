namespace QuantumUser.View.Managers
{
    using System.Collections;
    using Quantum;
    using UnityEngine;

    // Generic ground-landing-warning telegraph, decoupled from any specific enemy entity - listens
    // for EventProjectileLandingWarning (Position/Duration/Radius only, no owner) and pulls an instance
    // straight from TelegraphManager's pool, exactly the way EnemyAttackVisualsView does for a
    // caster's own windup telegraph (same TelegraphFade/TelegraphGrow prefab shape) - the only
    // difference is there's no owning enemy and no single-slot bookkeeping, since TelegraphManager's
    // pool already supports any number of simultaneous independent Get()/Release() instances on its
    // own. Mortar's barrage is the first consumer, but nothing here is projectile-specific - the
    // event only carries a point/duration/radius, so any future "several things are about to happen
    // at ground points, with a fair warning first" attack (a boss dropping a volley of spikes, an
    // AoE barrage with no real projectile at all) can fire the exact same event directly
    // (f.Events.ProjectileLandingWarning(point, authoredFuseTime, radius) - no flight-time math
    // needed when the duration is just an authored fuse) instead of building its own marker/telegraph
    // plumbing from scratch.
    public class GroundWarningTelegraphManager : MonoBehaviour
    {
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
            StartCoroutine(FadeOutAfter(fade, duration));
        }

        private IEnumerator FadeOutAfter(TelegraphFade fade, float duration)
        {
            yield return new WaitForSeconds(Mathf.Max(duration - fadeOutDuration, 0f));

            if (fade != null)
                fade.FadeOutAndRelease();
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
