using PrimeTween;
using QuantumUser.View.Managers;
using UnityEngine;
using UnityEngine.Serialization;

namespace Quantum
{
    // Pre-attached (by hand, in the Editor) to a TelegraphPrefab's root - owns everything about
    // one telegraph instance's lifecycle: fading its own SpriteRenderer in/out, kicking off an
    // optional pre-wired TelegraphGrow child's growth animation, and releasing itself back to
    // TelegraphManager's pool once fully faded out instead of being destroyed. Explicit
    // SerializeField references only - no GetComponent/GetComponentInChildren searches, so this
    // doesn't care how deep the prefab's own hierarchy is.
    //
    // Reused across TelegraphManager pool Get()/Release() cycles rather than recreated - every
    // field here gets fully reset in Initialize (called on every Get), nothing is one-time/
    // Awake-only except growthCircle's own resting-scale capture (see TelegraphGrow).
    public class TelegraphFade : MonoBehaviour
    {
        [SerializeField, Tooltip("The sprite that fades in/out. Required.")]
        private SpriteRenderer spriteRenderer;

        [SerializeField, Tooltip("Optional - a child sprite with its own TelegraphGrow that grows from 0 up to its authored scale over this telegraph's real duration. Leave empty if this telegraph has no growth-fill circle.")]
        private TelegraphGrow growthCircle;

        private GameObject _prefabKey;
        private float _fadeInDuration;
        private float _fadeOutDuration;
        private float _targetAlpha;
        private float _t;
        private bool _fadingOut;

        // Captured once, on Awake - not re-read in Initialize. spriteRenderer.color.a gets driven
        // down to 0 by every fade-out (see Update), so on a pooled instance's second-and-later
        // Initialize call, re-reading it there would just capture whatever the *previous* fade-out
        // left it at (0), making every subsequent fade-in lerp from 0 to 0. Same fix as
        // TelegraphGrow's resting-scale capture.
        private void Awake()
        {
            _targetAlpha = spriteRenderer != null ? spriteRenderer.color.a : 1f;
        }

        // prefabKey is whatever TelegraphData.TelegraphPrefab the caller (EnemyAttackVisualsView)
        // originally requested from TelegraphManager - passed in rather than a self-referencing
        // SerializeField so there's nothing on the prefab itself to misconfigure/forget to update
        // after duplicating it. enemyEntity is forwarded straight through to growthCircle - see its
        // own Initialize for why the growth animation needs it (anticipation-slow scaling).
        public void Initialize(GameObject prefabKey, float fadeInDuration, float fadeOutDuration, float growDuration, EntityRef enemyEntity)
        {
            _prefabKey = prefabKey;
            _fadeInDuration = Mathf.Max(fadeInDuration, 0.0001f);
            _fadeOutDuration = Mathf.Max(fadeOutDuration, 0.0001f);
            _t = 0f;
            _fadingOut = false;

            ApplyAlpha(0f);

            if (growthCircle != null)
                growthCircle.Initialize(growDuration, enemyEntity);
        }

        public void FadeOutAndRelease()
        {
            _fadingOut = true;
            _t = 0f;
        }

        private void Update()
        {
            if (spriteRenderer == null)
                return;

            float duration = _fadingOut == true ? _fadeOutDuration : _fadeInDuration;
            _t = Mathf.Clamp01(_t + Time.deltaTime / duration);
            float eased = Easing.Evaluate(_t, Ease.InOutSine);

            ApplyAlpha(_fadingOut == true ? Mathf.Lerp(_targetAlpha, 0f, eased) : Mathf.Lerp(0f, _targetAlpha, eased));

            if (_fadingOut == true && _t >= 1f)
            {
                Release();
            }
        }

        private void Release()
        {
            if (TelegraphManager.Instance != null && _prefabKey != null)
            {
                TelegraphManager.Instance.Release(_prefabKey, gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void ApplyAlpha(float alpha)
        {
            Color color = spriteRenderer.color;
            color.a = alpha;
            spriteRenderer.color = color;
        }
    }
}
