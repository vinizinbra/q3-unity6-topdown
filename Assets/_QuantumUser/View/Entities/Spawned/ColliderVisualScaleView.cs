using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Sizes a spawn's visual to the collider it actually ended up with. Quantum carries no scale
    // anywhere - Transform3D is position and rotation only, and QuantumEntityView syncs nothing else
    // - so a collider resized at runtime (SpawnEntitySkillAction's Scale, or FitToPath stretching a
    // box down the dash) stays invisible until the Unity side reads it back, which is what this does.
    //
    // Pairs with AreaDamage's promise that the shape which hurts is the shape you see.
    public class ColliderVisualScaleView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("The child transform to resize - not this entity's root, which QuantumEntityView drives.")]
        private Transform visual;

        [SerializeField, Tooltip("What the visual measures across at localScale 1. A Unity cube is 1,1,1; a quad or custom mesh may not be.")]
        private Vector3 visualUnitSize = Vector3.one;

        [SerializeField, Tooltip("Pop the visual in from zero to its resolved scale on spawn instead of snapping straight to it.")]
        private bool tweenScaleOnEnable;
        [SerializeField, ShowIf(nameof(tweenScaleOnEnable)), Tooltip("How long the pop-in takes.")]
        private float tweenDuration = 0.2f;
        [SerializeField, ShowIf(nameof(tweenScaleOnEnable)), Tooltip("Shapes the pop-in - OutBack overshoots past the resolved scale first, OutQuad/OutCubic decelerate into it.")]
        private Ease tweenEase = Ease.OutBack;

        // The collider is sized once at spawn and never resized after, so this stops once it takes.
        // It also keeps an unsupported shape from logging every frame.
        private bool _applied;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            _applied = false;
            TryApply(game);
        }

        // Initialize can land before the spawn's collider is readable; this is the retry.
        protected override void QUpdate(QuantumGame game)
        {
            TryApply(game);
        }

        private void TryApply(QuantumGame game)
        {
            if (_applied == true)
                return;

            // Predicted rather than Verified: a spawn created this tick has no verified frame yet,
            // and waiting for one would show it at its authored size first.
            if (game.Frames.Predicted.TryGet<PhysicsCollider3D>(_entityRef, out PhysicsCollider3D collider) == false)
                return;

            _applied = true;

            if (visual == null)
            {
                LogHelper.Error("ColliderVisualScaleView", $"'{name}' has no visual assigned.", this);
                return;
            }

            if (TryGetWorldSize(collider.Shape, out Vector3 worldSize) == false)
            {
                LogHelper.Error("ColliderVisualScaleView", $"'{name}' has a {collider.Shape.Type} collider, which has no size to read.", this);
                return;
            }

            var resolvedScale = new Vector3(
                Fit(worldSize.x, visualUnitSize.x),
                Fit(worldSize.y, visualUnitSize.y),
                Fit(worldSize.z, visualUnitSize.z));

            if (tweenScaleOnEnable == true)
            {
                visual.localScale = Vector3.zero;
                Tween.Scale(visual, resolvedScale, tweenDuration, tweenEase);
            }
            else
            {
                visual.localScale = resolvedScale;
            }
        }

        private static bool TryGetWorldSize(Shape3D shape, out Vector3 size)
        {
            switch (shape.Type)
            {
                case Shape3DType.Box:
                    // Box extents are half-sizes.
                    size = shape.Box.Extents.ToUnityVector3() * 2f;
                    return true;

                case Shape3DType.Sphere:
                    float diameter = shape.Sphere.Radius.AsFloat * 2f;
                    size = new Vector3(diameter, diameter, diameter);
                    return true;

                case Shape3DType.Capsule:
                    float width = shape.Capsule.Radius.AsFloat * 2f;
                    size = new Vector3(width, shape.Capsule.Extent.AsFloat * 2f, width);
                    return true;

                default:
                    size = Vector3.one;
                    return false;
            }
        }

        // An unset unit size is far likelier than an author meaning zero, which would divide the
        // visual out of existence.
        private static float Fit(float worldSize, float unitSize)
        {
            return Mathf.Approximately(unitSize, 0f) ? worldSize : worldSize / unitSize;
        }
    }
}
