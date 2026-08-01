using PrimeTween;
using QuantumUser.View.Managers;
using UnityEngine;

namespace Quantum
{
    // Owns everything about one hitscan pellet's visual: the line stretched from muzzle to
    // endpoint (faded out via PrimeTween, not destroyed - see IsPlaying/Play, this is reused from
    // WeaponView's tracer pool rather than a fresh instance per shot), a pooled particle burst at
    // the muzzle, and a pooled particle burst at the endpoint. Bundled together so a weapon's
    // tracer is one self-contained prefab an artist configures once, rather than splitting the
    // line here and the impact VFX on WeaponView.
    public class WeaponTracerView : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField, Tooltip("How long the tracer line stays visible before it fades out.")]
        private float duration = 0.05f;

        [SerializeField, Tooltip("Pooled particle effect (via EffectsManager) played at the muzzle - fires for every pellet, hit or miss. Leave empty for none.")]
        private ParticleSystem beginParticle;
        [SerializeField, Tooltip("Pooled particle effect (via EffectsManager) played at the resolved endpoint - only on an actual hit, skipped on a miss. Leave empty for none.")]
        private ParticleSystem endParticle;

        // Captured once rather than read live off line.startColor/endColor in Play() - once this
        // instance is reused (see WeaponView's tracer pool), the live color's alpha would already
        // be faded to 0 from the previous shot, so every replay after the first would tween from
        // (and stay at) fully transparent.
        private Color baseStartColor;
        private Color baseEndColor;
        private Tween colorTween;

        // True from the moment Play() starts until its fade tween completes - WeaponView's tracer
        // pool uses this to know which pooled instances are free to reuse for a new shot.
        public bool IsPlaying { get; private set; }

        private void Awake()
        {
            baseStartColor = line.startColor;
            baseEndColor = line.endColor;
        }

        public void Play(Vector3 origin, Vector3 endPoint, bool didHit)
        {
            IsPlaying = true;

            line.SetPosition(0, origin);
            line.SetPosition(1, endPoint);

            if (beginParticle != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(beginParticle, origin, Quaternion.identity);

            if (didHit == true && endParticle != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(endParticle, endPoint, Quaternion.identity);

            if (colorTween.isAlive == true)
                colorTween.Stop();

            colorTween = Tween.Custom(this, 1f, 0f, duration, (target, value) =>
            {
                target.line.startColor = SetAlpha(target.baseStartColor, value);
                target.line.endColor = SetAlpha(target.baseEndColor, value);
            }).OnComplete(() => IsPlaying = false);
        }

        private static Color SetAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }
    }
}
