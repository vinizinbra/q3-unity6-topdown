namespace Quantum
{
    using UnityEngine;

    // Tumbles a thrown projectile's visual (Dono do Céu's crowd trash) around its own facing axis while it flies -
    // purely cosmetic. Pool-safe: ResetForPool puts the authored rotation back (ProjectileViewSnapshot also restores
    // local transforms, this just keeps the spin phase from carrying into the next shot).
    public class ProjectileSpinView : MonoBehaviour, IProjectilePoolPart
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector2 degreesPerSecond = new Vector2(420f, 720f);

        private Quaternion _baseRotation;
        private float _speed;

        private void Awake()
        {
            if (target == null)
                target = transform;

            _baseRotation = target.localRotation;
            PickSpeed();
        }

        private void OnEnable()
        {
            PickSpeed();
        }

        private void PickSpeed()
        {
            // View-only randomness (cosmetic), never read back by the simulation.
            _speed = Random.Range(degreesPerSecond.x, degreesPerSecond.y) * (Random.value < 0.5f ? -1f : 1f);
        }

        private void Update()
        {
            target.localRotation *= Quaternion.AngleAxis(_speed * Time.deltaTime, Vector3.forward);
        }

        public void ResetForPool()
        {
            if (target != null)
                target.localRotation = _baseRotation;

            PickSpeed();
        }
    }
}
