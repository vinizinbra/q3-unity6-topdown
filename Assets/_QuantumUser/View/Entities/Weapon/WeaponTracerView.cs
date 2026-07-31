using PrimeTween;
using QuantumUser.View.Managers;
using UnityEngine;

namespace Quantum
{
    // Spawned fresh per hitscan pellet by WeaponView (see its EventHitscanFired handler) - owns
    // everything about that one pellet's visual: the line stretched from muzzle to endpoint (then
    // faded out and destroyed, not pooled - needs a per-instance LineRenderer rather than a
    // restartable ParticleSystem), a pooled particle burst at the muzzle, and a pooled particle
    // burst at the endpoint. Bundled together so a weapon's tracer is one self-contained prefab an
    // artist configures once, rather than splitting the line here and the impact VFX on WeaponView.
    public class WeaponTracerView : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField, Tooltip("How long the tracer line stays visible before it's destroyed.")]
        private float duration = 0.05f;

        [SerializeField, Tooltip("Pooled particle effect (via EffectsManager) played at the muzzle - fires for every pellet, hit or miss. Leave empty for none.")]
        private ParticleSystem beginParticle;
        [SerializeField, Tooltip("Pooled particle effect (via EffectsManager) played at the resolved endpoint - only on an actual hit, skipped on a miss. Leave empty for none.")]
        private ParticleSystem endParticle;

        public void Play(Vector3 origin, Vector3 endPoint, bool didHit)
        {
            line.SetPosition(0, origin);
            line.SetPosition(1, endPoint);

            if (beginParticle != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(beginParticle, origin, Quaternion.identity);

            if (didHit == true && endParticle != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(endParticle, endPoint, Quaternion.identity);

            Color startColor = line.startColor;
            Color endColor = line.endColor;

            Tween.Custom(this, 1f, 0f, duration, (target, value) =>
            {
                target.line.startColor = SetAlpha(startColor, value);
                target.line.endColor = SetAlpha(endColor, value);
            }).OnComplete(() => Destroy(gameObject));
        }

        private static Color SetAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }
    }
}
