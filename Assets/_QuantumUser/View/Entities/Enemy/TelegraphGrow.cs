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

        private void Awake()
        {
            _restingScale = transform.localScale;
        }

        public void Initialize(float duration)
        {
            _duration = Mathf.Max(duration, 0.0001f);
            _t = 0f;
            _active = true;
            transform.localScale = ComputeScale(0f);
        }

        private void Update()
        {
            if (_active == false || _t >= 1f)
                return;

            _t = Mathf.Clamp01(_t + Time.deltaTime / _duration);
            transform.localScale = ComputeScale(_t);
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
