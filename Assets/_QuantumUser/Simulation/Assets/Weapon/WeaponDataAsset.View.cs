namespace Quantum
{
    using UnityEngine;

    // Magnitude bucket for the camera shake fired on PlayerFired - same "one shared enum, several
    // tiers of values" idea as EffectConfig.KnockbackTier. Lives here (not WeaponDataAsset.cs) since
    // shake is purely presentational - the actual amplitude/duration numbers live on the Unity-only
    // CameraShakeConfig, resolved by WeaponCameraShakeListener.
    public enum WeaponShakeTier
    {
        Small,
        Medium,
        Strong
    }

    // View-only half of WeaponDataAsset (see the partial declaration in WeaponDataAsset.cs).
    public partial class WeaponDataAsset
    {
        [Tooltip("Prefab instantiated under the player's weapon socket to represent this weapon - must have a WeaponView component. WeaponViewController resolves this directly, no separate catalog/lookup needed.")]
        public GameObject ViewPrefab;

        // A Choose-Weapon level-up/Chest card (WeaponCardWidget) needs an icon, but every weapon
        // already has a real world sprite authored on ViewPrefab's own root SpriteRenderer (see
        // e.g. BasicWeapon.prefab) - reusing that instead of a second hand-authored Icon field
        // means zero extra per-weapon authoring. Safe to read directly off the prefab ASSET (no
        // Instantiate needed) since the SpriteRenderer is a plain sibling component wired in the
        // Inspector, not something WeaponView itself builds up at runtime.
        public Sprite GetIcon()
        {
            return ViewPrefab != null ? ViewPrefab.GetComponent<SpriteRenderer>()?.sprite : null;
        }

        [Tooltip("Camera shake tier applied (to the local player only) each time this weapon fires - see WeaponCameraShakeListener/CameraShakeConfig.")]
        public WeaponShakeTier ShakeTier = WeaponShakeTier.Small;

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
