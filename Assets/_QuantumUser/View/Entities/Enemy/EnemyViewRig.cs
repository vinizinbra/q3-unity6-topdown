using UnityEngine;

namespace Quantum
{
    // Single named-reference point for an EnemyDataAsset.ViewPrefab's rig transforms - lets
    // EnemyBlobAnimationView/EnemyArmAimView/etc. each hold a sibling reference to this instead of
    // separately declaring and re-wiring their own root/arm Transform fields per enemy type.
    public class EnemyViewRig : MonoBehaviour
    {
        [SerializeField, Tooltip("The sprite EnemyView.SpawnSprite measures (via sprite.bounds, already Pixels-Per-Unit-corrected) to compute this instance's fit scale - so the rig always ends up the same apparent size for a given EnemyDataAsset.Radius regardless of what PPU this sprite happens to be imported at. Leave empty to use whichever SpriteRenderer is on this same GameObject (EnemyRoot).")]
        private SpriteRenderer referenceSprite;
        [SerializeField] private Transform head;
        [SerializeField] private Transform torso;
        [SerializeField, Tooltip("Animated by EnemyBlobAnimationView's phase-triggered ArmSwingBack/ArmSnap tells only - never touched by EnemyArmAimView's continuous aim tracking (that targets Gun below instead), so a rig can use both without them fighting over the same transform. Leave empty for a rig with no arm-only tell (Gun alone, or neither).")]
        private Transform arm;
        [SerializeField, Tooltip("Continuously aimed at Enemy.Target by EnemyArmAimView, and used by EnemyAttackVisualsView.SpawnStepParticle as the parent for a Parented particle. Point this at the same transform as Arm above for a rig with no separate gun sprite (e.g. ScavengerHunt-Ranged); leave empty for a melee rig that only needs Arm's own swing tell and no continuous aim tracking at all (e.g. ScavengerHunt-Slammer).")]
        private Transform gun;
        [SerializeField, Tooltip("Particle system restarted on each shot by EnemyArmAimView.Fire() - an explicit reference rather than a GetComponentInChildren(gun) search, since a weapon could have more than one particle child (e.g. a muzzle flash AND a separate shell-eject) and searching would arbitrarily grab whichever comes first in the hierarchy. Leave empty for a shooter enemy with no muzzle particle.")]
        private ParticleSystem muzzle;
        [SerializeField, Tooltip("Particle system restarted at the start of the windup (EnemyArmAimView.PlayPreShoot, called on AttackPhase.Anticipation) - a distinct charge-up/telegraph effect that plays BEFORE the shot, separate from muzzle above which plays AT the shot. Leave empty for a shooter enemy with no pre-shoot tell.")]
        private ParticleSystem preShootMuzzle;
        [SerializeField, Tooltip("Degrees added so the gun's own rest orientation lines up with true aim angle 0. -90 if the gun art is drawn pointing up rather than right. Per-rig rather than a single field on EnemyArmAimView, since that component is one shared instance living on the generic enemy prototype (see its own class comment) - a single shared offset couldn't be correct for every enemy type's own art at once.")]
        private float armAngleOffset;

        // Shoot-kick tuning (EnemyArmAimView.Fire(), applied additively on top of the aim angle,
        // not a separate arm-swing animation - see that method's own comment on why this is the
        // correct "shoot tell" mechanism for an enemy with continuous aim tracking, unlike
        // EnemyBlobAnimationView's ArmSwingBack/ArmSnap which would fight the aim rotation for
        // control of the same transform every frame). Per-rig for the same reason armAngleOffset
        // is above - one enemy's weapon might want a sharp snappy kick, another a heavy slow one.
        [Header("Shoot Recoil")]
        [SerializeField, Tooltip("Degrees the gun kicks on each shot, additive on top of the aim angle.")]
        private float recoilKickDegrees = 8f;
        [SerializeField, Tooltip("Local-plane distance the gun snaps back on each shot, opposite its current aim direction.")]
        private float recoilKickDistance = 0.08f;
        [SerializeField, Tooltip("How long each shot's kick takes to fully settle back to rest. Keep at or below the enemy's fire interval if rapid fire shouldn't visibly reset mid-decay.")]
        private float recoilDuration = 0.15f;
        [SerializeField, Tooltip("Oscillations per second as the kick settles.")]
        private float recoilFrequency = 16f;
        [SerializeField, Range(0f, 1f), Tooltip("0 = full recoil (kicks back, swings past rest, settles), 1 = no recoil (eases straight back to rest with no overshoot). Shared by the rotation and position kicks.")]
        private float recoilAsymmetry = 0.3f;

        // EnemyViewRig sits on ViewPrefab's own root, so that root doubles as EnemyRoot - no
        // separate self-reference field to wire. This is the same transform EnemyView.SpawnSprite
        // positions/scales at spawn (bottom-pivot offset, radius scale), so
        // EnemyBlobAnimationView.CacheBaseline (which runs after that, see SetRig) picks up those
        // spawn-time values as its baseline automatically.
        public Transform EnemyRoot => transform;
        public SpriteRenderer ReferenceSprite => referenceSprite != null ? referenceSprite : GetComponent<SpriteRenderer>();
        public Transform Head => head;
        public Transform Torso => torso;
        public Transform Arm => arm;
        public Transform Gun => gun;
        public ParticleSystem Muzzle => muzzle;
        public ParticleSystem PreShootMuzzle => preShootMuzzle;
        public float ArmAngleOffset => armAngleOffset;
        public float RecoilKickDegrees => recoilKickDegrees;
        public float RecoilKickDistance => recoilKickDistance;
        public float RecoilDuration => recoilDuration;
        public float RecoilFrequency => recoilFrequency;
        public float RecoilAsymmetry => recoilAsymmetry;

        // Gun's authored rest pose, captured once here rather than read live by whichever sibling
        // needs it (EnemyArmAimView._gunBaseLocalPosition) - this GameObject is pooled
        // (ViewPrefabPool), and Release() only SetActive(false)s/reparents it, it never resets
        // child transforms back to rest. Awake only fires once, on the pool's original Instantiate,
        // never again on a later Get()/Release() reuse - so this is the one point guaranteed to see
        // gun still at its true rest pose, before any aiming/recoil has ever touched it. Reading
        // gun.localPosition live instead would bake in whatever offset a PREVIOUS enemy's rig left
        // behind (e.g. died mid-recoil-kick) as this enemy's permanent "rest" position.
        public Vector3 GunBaseLocalPosition { get; private set; }

        private void Awake()
        {
            if (gun != null)
                GunBaseLocalPosition = gun.localPosition;
        }

        // Every SpriteRenderer under this rig, gathered fresh each call rather than serialized -
        // HitFeedback.SetRig reads this to know what to flash without every ViewPrefab needing an
        // extra Inspector-wired array kept in sync with whatever renderers that enemy type happens
        // to have.
        public SpriteRenderer[] Sprites => GetComponentsInChildren<SpriteRenderer>(true);
    }
}
