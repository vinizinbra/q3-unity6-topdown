using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Visual tension for Pixie's thrown bombs (BunnyBomb, cluster bomblets, fireworks) as
    // Projectile.RemainingLifetime counts down toward AreaHitData.ApplyExpire - a punch on every
    // whole second that ticks off, then a shake for the last ShakeLeadTime seconds before it goes
    // off. Also swaps in a decoy sprite while BirthdayCakeSkillAction's Decoy tag is present.
    // Targets visualRoot rather than this entity's own transform, since QuantumEntityView drives
    // that transform's position every frame and would fight a position-based shake tween.
    public class PixieBombView : CustomQuantumEntityViewComponent
    {
        [SerializeField] private Transform visualRoot;

        [Header("Punch (once per whole second remaining)")]
        [SerializeField] private float punchStrength = 0.15f;
        [SerializeField] private float punchDuration = 0.25f;

        [Header("Shake (final stretch before detonation)")]
        [SerializeField] private float shakeLeadTime = 0.3f;
        [SerializeField] private float shakePositionStrength = 0.08f;
        [SerializeField] private float shakeScaleStrength = 0.1f;
        [SerializeField, Tooltip("Number of shakes per second, shared by both the position and scale shake.")]
        private float shakeFrequency = 10f;

        [Header("Decoy sprite swap")]
        [Tooltip("Leave empty to disable the swap - this projectile has no decoy variant.")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite decoySprite;

        private int _lastWholeSecondRemaining = -1;
        private bool _hasShaken;
        private Sprite _defaultSprite;
        private bool? _isDecoy;

        public override void Awake()
        {
            base.Awake();

            if (spriteRenderer != null)
                _defaultSprite = spriteRenderer.sprite;
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _lastWholeSecondRemaining = -1;
            _hasShaken = false;
            _isDecoy = null;
        }

        // Shake() is timed to still be running right up to detonation - the projectile (and this
        // view's GameObject) is destroyed the same tick, which without this orphans the shake tween
        // on visualRoot and makes PrimeTween log a stack-trace-capturing error every detonation.
        public override void OnDestroy()
        {
            base.OnDestroy();

            if (visualRoot != null)
                Tween.StopAll(visualRoot);
        }

        protected override void QUpdate(QuantumGame game)
        {
            var frame = game.Frames.Predicted;
            TickDecoySprite(frame);

            if (frame.Has<DestroyAfterTime>(_entityRef) == false)
                return;


            if (visualRoot == null)
                return;

            float remaining = frame.Get<DestroyAfterTime>(_entityRef).RemainingTime.AsFloat;

            TickPunch(remaining);
            TickShake(remaining);
        }

        // BirthdayCakeSkillAction's DecoyOnThrowUpgrade adds Decoy onto the projectile right after
        // it spawns (see ProjectileSkillData.Fire) rather than at spawn time itself, so this can't
        // just be a one-shot check in Initialize - it has to keep watching until it flips.
        private void TickDecoySprite(Frame frame)
        {
            if (spriteRenderer == null || decoySprite == null)
                return;

            bool isDecoy = frame.Has<Decoy>(_entityRef);

            if (_isDecoy == isDecoy)
                return;

            _isDecoy = isDecoy;
            spriteRenderer.sprite = isDecoy == true ? decoySprite : _defaultSprite;
        }

        private void TickPunch(float remaining)
        {
            int wholeSecondRemaining = Mathf.CeilToInt(remaining);

            // First read after (re)spawn just establishes the baseline - punching immediately on
            // spawn would double up with whatever throw VFX already plays that instant.
            if (_lastWholeSecondRemaining < 0)
            {
                _lastWholeSecondRemaining = wholeSecondRemaining;
                return;
            }

            if (wholeSecondRemaining >= _lastWholeSecondRemaining)
                return;

            _lastWholeSecondRemaining = wholeSecondRemaining;
            Punch();
        }

        private void TickShake(float remaining)
        {
            if (_hasShaken == true || remaining > shakeLeadTime)
                return;

            _hasShaken = true;
            Shake();
        }

        [Button]
        public void Punch()
        {
            Tween.PunchScale(visualRoot, punchStrength * Vector3.one, punchDuration);
        }

        [Button]
        public void Shake()
        {
            Tween.ShakeLocalPosition(visualRoot, shakePositionStrength * Vector3.one, shakeLeadTime, shakeFrequency);
            Tween.ShakeScale(visualRoot, shakeScaleStrength * Vector3.one, shakeLeadTime, shakeFrequency);
        }
    }
}
