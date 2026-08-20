using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // View companion for a Traversal Challenge platform (TraversalChallenge.qtn/
    // docs/traversal-challenge.md) - f.Create/f.Destroy'd at runtime, no gameplay component of its
    // own. cubeVisualBuilder's pivot sits at its own min corner, not its center (see
    // CubeVisualBuilder's own class comment - "assumes each cube's pivot sits at its bottom min
    // corner"), so animating it directly would shake/sink asymmetrically from that corner instead
    // of looking centered. Solves it by creating a runtime pivot Transform (parentless from the
    // start, never a child of the Quantum entity) positioned at visualCollider.bounds.center (read
    // BEFORE Generate() runs - Generate() destroys and recreates that collider component, so the
    // reference goes stale the instant it's called), then moving cubeVisualBuilder's whole
    // GameObject off the entity root and onto that pivot (SetParent(..., worldPositionStays: true) -
    // this also happens to correct cubeVisualBuilder's own transform.localScale from the entity
    // root's authored (1,1,1)-under-a-(4,4,4)-scaled-parent down to a plain (4,4,4) it can read
    // directly, so Generate()'s own grid-size math needs no separate manual fix). Fully detaching the
    // visual from the entity root - not just animating a child in place - matters because
    // QuantumEntityView destroys/pools the entity's own GameObject the instant f.Destroy fires in the
    // simulation, which would otherwise kill this pivot (and any in-flight tween on it) before the
    // destroy animation ever gets to play.
    public class TraversalPlatformView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("The child CubeVisualBuilder that generates this platform's real mesh (e.g. the 'VisualCube' child) - its own generateOnEnable is left off, Generate() is called explicitly here instead so it always runs exactly once per spawn regardless of pooling.")]
        private CubeVisualBuilder cubeVisualBuilder;

        [SerializeField, Tooltip("The BoxCollider sitting on cubeVisualBuilder's own GameObject before Generate() runs - read once for its world-space bounds.center (the platform's true geometric center), since cubeVisualBuilder's own transform is a corner pivot, not a center one.")]
        private BoxCollider visualCollider;

        [SerializeField] private float riseDistance = 2f;
        [SerializeField] private float riseDuration = 0.35f;
        [SerializeField] private Ease riseEase = Ease.OutBack;

        [SerializeField] private Vector3 destroyShakeStrength = new Vector3(0.15f, 0.15f, 0.15f);
        [SerializeField] private float destroyShakeDuration = 0.2f;
        [SerializeField] private float destroySinkDistance = 2f;
        [SerializeField] private float destroySinkDuration = 0.3f;

        private Transform _pivot;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            if (cubeVisualBuilder == null || visualCollider == null)
            {
                LogHelper.Warn("TraversalPlatformView", "cubeVisualBuilder/visualCollider not assigned - skipping visual spawn.", this);
                return;
            }

            Vector3 center = visualCollider.bounds.center;

            _pivot = new GameObject("TraversalPlatformVisual").transform;
            _pivot.SetPositionAndRotation(center, cubeVisualBuilder.transform.rotation);

            cubeVisualBuilder.transform.SetParent(_pivot, worldPositionStays: true);

            cubeVisualBuilder.Generate();

            Vector3 targetPosition = _pivot.position;
            _pivot.position = targetPosition + Vector3.down * riseDistance;
            Tween.Position(_pivot, targetPosition, riseDuration, riseEase);
        }

        public override void DeInitialize(QuantumGame game)
        {
            PlayDestroySequence();
            base.DeInitialize(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void PlayDestroySequence()
        {
            if (_pivot == null)
            {
                return;
            }

            Transform pivot = _pivot;
            _pivot = null;

            Tween.ShakeLocalPosition(pivot, destroyShakeStrength, destroyShakeDuration)
                .OnComplete(() =>
                {
                    Vector3 sunkPosition = pivot.position + Vector3.down * destroySinkDistance;
                    Tween.Position(pivot, sunkPosition, destroySinkDuration, Ease.InQuad)
                        .OnComplete(() => Destroy(pivot.gameObject));
                });
        }
    }
}
