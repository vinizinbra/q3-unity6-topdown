namespace Quantum
{
    using UnityEngine;

    // View-only half of WeaponDataAsset (see the partial declaration in WeaponDataAsset.cs).
    public partial class WeaponDataAsset
    {
        [Tooltip("Prefab instantiated under the player's weapon socket to represent this weapon - must have a WeaponView component. WeaponViewController resolves this directly, no separate catalog/lookup needed.")]
        public GameObject ViewPrefab;

        // Editor-only preview, not read by simulation or any other View code - just a quick sanity
        // check while tuning Damage/FireRate/MagazineSize/ReloadDuration together in the Inspector.
        // Recomputed on every value change (OnValidate) and on load (OnEnable) rather than being an
        // editable field, so it can never drift out of sync with the stats above it.
        [Header("Preview")]
        [Tooltip("Recomputed automatically - not an input. Burst ignores reload (Damage x Pellets x FireRate); Sustained folds in how long a full magazine + reload actually takes, so a small mag/long reload weapon reads lower here than its burst number alone would suggest.")]
        [TextArea(2, 3)]
        [SerializeField]
        private string _dpsPreview;

        private void OnEnable()
        {
            _dpsPreview = BuildDpsPreview();
        }

        private void OnValidate()
        {
            _dpsPreview = BuildDpsPreview();
        }

        private string BuildDpsPreview()
        {
            float fireRate = FireRate.AsFloat;

            if (fireRate <= 0f)
                return "DPS: n/a (FireRate is 0)";

            float damagePerShot = Damage.AsFloat * Mathf.Max(1, PelletCount);
            float burstDps = damagePerShot * fireRate;

            if (MagazineSize <= 0)
                return $"Burst DPS: {burstDps:0.#} (MagazineSize is 0 - can never fire)";

            float magazineDuration = MagazineSize / fireRate;
            float cycleDuration = magazineDuration + Mathf.Max(0f, ReloadDuration.AsFloat);
            float sustainedDps = damagePerShot * MagazineSize / cycleDuration;

            return $"Burst DPS: {burstDps:0.#}\nSustained DPS (incl. reload): {sustainedDps:0.#}\nDamage/min: {sustainedDps * 60f:0}";
        }
    }
}
