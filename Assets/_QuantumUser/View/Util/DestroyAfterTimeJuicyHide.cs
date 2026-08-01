using NaughtyAttributes;
using PrimeTween;
using Quantum;
using UnityEngine;

namespace QuantumUser.View.Util
{
    // Generic "about to vanish" juice for any entity carrying the generic DestroyAfterTime
    // component (Vortex, TimeWall, BurningGround, ...) - shrinks the visual away right before
    // DestroyAfterTimeSystem actually destroys it. DestroyAfterTime only exposes a live countdown
    // (no total/original duration, no destroy event - see DestroyAfterTimeSystem), so this polls
    // RemainingTime every frame and fires once it drops under hideLeadTime, same approach as the
    // shake/scale-down in DespawnAnimationView but as a single lightweight PrimeTween shrink
    // instead of a bespoke two-phase curve - use this where that's overkill.
    public class DestroyAfterTimeJuicyHide : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Transform that shrinks away. Defaults to this component's own transform if left unassigned.")]
        private Transform visual;

        [SerializeField, Tooltip("Starts the shrink once DestroyAfterTime.RemainingTime drops to/under this many seconds.")]
        private float hideLeadTime = 0.45f;
        [SerializeField] private float hideDuration = 0.45f;
        [SerializeField, Tooltip("InBack overshoots into a little squash before shrinking away - free anticipation juice.")]
        private Ease hideEase = Ease.InBack;

        private Vector3 _baseScale;
        private bool _hiding;
        private Tween _tween;

        public override void Awake()
        {
            base.Awake();

            if (visual == null)
                visual = transform;

            _baseScale = visual.localScale;
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _tween.Stop();
            visual.localScale = _baseScale;
            _hiding = false;
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (_hiding)
                return;

            Frame frame = game.Frames.Predicted;
            if (frame == null || frame.TryGet<DestroyAfterTime>(_entityRef, out DestroyAfterTime lifetime) == false)
                return;

            if (lifetime.RemainingTime.AsFloat <= hideLeadTime)
                PlayHide();
        }

        [Button("Preview Hide")]
        private void PlayHide()
        {
            _hiding = true;
            _tween.Stop();
            visual.localScale = _baseScale;
            _tween = Tween.Scale(visual, Vector3.zero, hideDuration, hideEase);
        }
    }
}
