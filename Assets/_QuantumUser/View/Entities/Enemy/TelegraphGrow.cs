using UnityEngine;

namespace Quantum
{
    // Pre-authored (attached by hand, in the Editor) to a child sprite nested under a
    // TelegraphPrefab's root, and referenced directly by that root's TelegraphFade - no
    // GetComponent/GetComponentInChildren search needed anywhere.
    //
    // Captures whatever local scale it was authored at as its "fully grown" resting scale once,
    // on Awake - this doesn't change across TelegraphManager pool Get()/Release() cycles, so it's
    // safe to only read it the one time the GameObject is actually created. Initialize resets the
    // animation itself (progress + scale back to the t=0 pose) on every call, so growth correctly
    // replays from scratch each time this is reused from the pool.
    public class TelegraphGrow : MonoBehaviour
    {
        [Header("Grow Axes")]
        [SerializeField, Tooltip("Which axes actually animate from 0 up to their resting scale. An axis left unchecked stays at its full resting scale immediately, the whole time - e.g. enable only Y to grow a lane's length while its width stays fixed, or enable both X and Y for a square/circle inflating uniformly from center.")]
        private bool growX = true;
        [SerializeField] private bool growY = true;
        [SerializeField, Tooltip("Usually left off - most telegraphs are flat decals where Z is just sprite depth/thickness (typically resting at 1), which has no visible effect whether it's grown or held constant.")]
        private bool growZ;

        private Vector3 _restingScale;
        private float _duration = 1f;
        private float _t;
        private bool _active;
        private EntityRef _enemyEntity;

        private void Awake()
        {
            _restingScale = transform.localScale;
        }

        // duration comes from the enemy's own remaining EnemyActionData.AnticipationTime at spawn
        // (see EnemyAttackVisualsView.SpawnTelegraph) - in the SAME abstract units as
        // Enemy.StateTimer, not literal real seconds. Those units only equal real seconds when the
        // enemy's own anticipation-slow multiplier is 1 (unfrozen); enemyEntity lets Update below
        // read that multiplier live every frame (StatusEffectUtility.GetAnticipationMultiplier) so
        // the growth rate itself stretches in lockstep with a Freeze applied before OR during this
        // telegraph's growth, instead of only ever matching real time.
        public void Initialize(float duration, EntityRef enemyEntity)
        {
            _duration = Mathf.Max(duration, 0.0001f);
            _t = 0f;
            _active = true;
            _enemyEntity = enemyEntity;
            transform.localScale = ComputeScale(0f);
        }

        private void Update()
        {
            if (_active == false || _t >= 1f)
                return;

            _t = Mathf.Clamp01(_t + (Time.deltaTime * ResolveAnticipationMultiplier()) / _duration);
            transform.localScale = ComputeScale(_t);
        }

        // Same live-read pattern as EnemyBlobAnimationView's own anticipation-slow scaling, just
        // reached via QuantumRunner.Default directly (see PlayerManager/MyLocalPlayer for the same
        // idiom) since this is a plain pooled MonoBehaviour, not a QuantumEntityViewComponent with
        // its own per-frame Frame parameter to read instead. Defaults to no slowdown (1) if the
        // runner/frame isn't available for any reason, rather than stalling growth entirely. Also
        // folds in Shock's Stagger (multiplier 0, a full pause rather than a stretch) so the ground
        // decal doesn't keep growing to completion while EnemyBlobAnimationView's own body animation
        // freezes for the same windup - see that component's identical IsStaggered check.
        private float ResolveAnticipationMultiplier()
        {
            QuantumGame game = QuantumRunner.Default != null ? QuantumRunner.Default.Game : null;
            Frame frame = game?.Frames.Predicted;

            if (frame == null)
                return 1f;

            float staggerMultiplier = StatusEffectUtility.IsStaggered(frame, _enemyEntity) == true ? 0f : 1f;
            return StatusEffectUtility.GetAnticipationMultiplier(frame, _enemyEntity).AsFloat * staggerMultiplier;
        }

        private Vector3 ComputeScale(float t)
        {
            return new Vector3(
                growX ? Mathf.LerpUnclamped(0f, _restingScale.x, t) : _restingScale.x,
                growY ? Mathf.LerpUnclamped(0f, _restingScale.y, t) : _restingScale.y,
                growZ ? Mathf.LerpUnclamped(0f, _restingScale.z, t) : _restingScale.z);
        }
    }
}
