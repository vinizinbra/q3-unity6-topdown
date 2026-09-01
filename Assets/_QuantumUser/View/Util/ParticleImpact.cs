using UnityEngine;

namespace Quantum
{
    // Hand-authored on any impact/hit particle prefab (independent of AttackVisualStep.ShakeImpact -
    // that one only covers enemy attack-phase particles; this covers ANY particle, wherever it's
    // spawned from - a weapon perk explosion, a skill hit effect, a boss slam, etc.) to shake
    // FollowCamera, with the same distance-falloff math (FollowCamera.ShakeAtPosition), at this
    // particle's own spawn position.
    //
    // Reads position from Update() rather than OnEnable() - EffectsManager's pool (ObjectPool<T>)
    // reactivates a reused instance (SetActive(true), firing OnEnable) BEFORE repositioning it to
    // this play's actual spawn point (see EffectsManager.GetPooledInstance), so transform.position
    // at OnEnable time would still be wherever this instance last played. Deferring one Update -
    // same frame's position write, next frame's read - is an imperceptible delay for an impact
    // shake and guarantees the position is already correct by then, pooled or freshly instantiated
    // either way.
    public class ParticleImpact : MonoBehaviour
    {
        [SerializeField, Tooltip("0 = no shake. Same distance-falloff math AttackVisualStep.ShakeImpact uses - see FollowCamera.ShakeAtPosition.")]
        private float shakeImpact = 0.5f;

        private bool _pendingShake;

        private void OnEnable()
        {
            _pendingShake = shakeImpact > 0f;
        }

        private void Update()
        {
            if (_pendingShake == false)
                return;

            _pendingShake = false;

            if (FollowCamera.I != null)
                FollowCamera.I.ShakeAtPosition(transform.position, shakeImpact);
        }
    }
}
