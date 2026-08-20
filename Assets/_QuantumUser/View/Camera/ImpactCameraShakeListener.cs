using NaughtyAttributes;
using Quantum;
using UnityEngine;

namespace QuantumUser.View
{
    // Shakes FollowCamera on heavy physical-impact moments, filtered to a LOCAL player's own impacts -
    // a remote Brute slamming down across the map shouldn't rattle this client's camera. Same
    // "local entity" filter WeaponCameraShakeListener/DamageFeedbackManager already use.
    //
    // Deliberately generic rather than one listener per Ascension: it keys off the shared, source-
    // agnostic events (GroundbreakerSlammed, WallSlammed - the latter fired by WallSlamUtility itself,
    // so Brute's Iron Shoulder dash gets this for free alongside his Groundbreaker landing), which is
    // also why it doesn't live in either Ascension's own code. Anything added later that routes through
    // those events picks this up with no extra hookup.
    //
    // Tuning lives directly on this component rather than in CameraShakeConfig - that asset is a
    // per-WeaponShakeTier lookup, a vocabulary that doesn't describe a landing or a wall impact.
    public class ImpactCameraShakeListener : QuantumGlobalMonoBehaviour
    {
        [Header("Groundbreaker landing")]
        [SerializeField, Tooltip("Turn off to drop the landing shake entirely without unwiring this component.")]
        private bool shakeOnGroundbreaker = true;
        [SerializeField, Tooltip("Shake amplitude at referenceRadius. Scaled linearly by the landing's own radius, so rank 3 (4.5) hits noticeably harder than ranks 1-2 (3) off one authored value instead of three.")]
        private float groundbreakerAmplitude = 0.3f;
        [SerializeField, Tooltip("Radius that groundbreakerAmplitude is authored against - Groundbreaker rank 1/2's own ImpactRadius. A landing at exactly this radius shakes at exactly that amplitude.")]
        private float groundbreakerReferenceRadius = 3f;
        [SerializeField, Tooltip("Ceiling on the radius-scaled amplitude, so a future rank or a retuned radius can't produce an unplayable camera.")]
        private float groundbreakerMaxAmplitude = 0.5f;
        [SerializeField] private float groundbreakerDuration = 0.28f;
        [SerializeField] private float groundbreakerFrequency = 22f;

        [Header("Wall slam")]
        [SerializeField, Tooltip("Turn off to drop the wall-slam shake entirely. Worth doing if Iron Shoulder's dash (which can slam several enemies in one sweep) ends up feeling noisy - FollowCamera.Shake already ignores a weaker shake while a stronger one runs, so they don't compound, but they do re-trigger.")]
        private bool shakeOnWallSlam = true;
        [SerializeField, Tooltip("Used when the Stun did NOT land - the target hit the wall but resisted (hard-CC immunity window, or an ImmuneToHardCC tier). Deliberately lighter than the stunned case.")]
        private float wallSlamAmplitude = 0.12f;
        [SerializeField, Tooltip("Used when the Stun genuinely landed - the moment that actually pays off, and the one that opens Groundbreaker rank 3's Exposed window.")]
        private float wallSlamStunnedAmplitude = 0.2f;
        [SerializeField] private float wallSlamDuration = 0.14f;
        [SerializeField] private float wallSlamFrequency = 24f;

        public override void QStart(QuantumGame game)
        {
            QuantumEvent.Subscribe<EventGroundbreakerSlammed>(this, OnGroundbreakerSlammed);
            QuantumEvent.Subscribe<EventWallSlammed>(this, OnWallSlammed);
        }

        private void OnDestroy()
        {
            QuantumEvent.UnsubscribeListener(this);
        }

        public override void QUpdate(QuantumGame game)
        {
        }

        public override void QLateUpdate(QuantumGame game)
        {
        }

        private void OnGroundbreakerSlammed(EventGroundbreakerSlammed e)
        {
            if (shakeOnGroundbreaker == false || IsLocal(e.Owner) == false)
                return;

            // Guarded against a zero/negative authored reference radius rather than trusting the
            // Inspector - a divide by zero here would push amplitude to Infinity and lock the camera.
            float scale = groundbreakerReferenceRadius > 0f
                ? e.Radius.AsFloat / groundbreakerReferenceRadius
                : 1f;

            float amplitude = Mathf.Min(groundbreakerAmplitude * scale, groundbreakerMaxAmplitude);

            Shake(amplitude, groundbreakerDuration, groundbreakerFrequency);
        }

        // e.Owner is the knockback SOURCE (the player whose dash or landing did the shoving), not the
        // enemy that hit the wall - so this correctly shakes only for whoever caused it.
        private void OnWallSlammed(EventWallSlammed e)
        {
            if (shakeOnWallSlam == false || IsLocal(e.Owner) == false)
                return;

            float amplitude = e.Stunned == true ? wallSlamStunnedAmplitude : wallSlamAmplitude;

            Shake(amplitude, wallSlamDuration, wallSlamFrequency);
        }

        private static bool IsLocal(EntityRef entity)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.AnyLocalPlayerSetup == false)
                return false;

            return MyLocalPlayer.Instance.IsLocalEntity(entity);
        }

        private static void Shake(float amplitude, float duration, float frequency)
        {
            if (FollowCamera.I == null)
                return;

            FollowCamera.I.Shake(amplitude, duration, frequency);
        }

        [Button("Test Groundbreaker Landing")]
        public void TestGroundbreakerShake() => Shake(groundbreakerAmplitude, groundbreakerDuration, groundbreakerFrequency);

        [Button("Test Wall Slam (stunned)")]
        public void TestWallSlamShake() => Shake(wallSlamStunnedAmplitude, wallSlamDuration, wallSlamFrequency);
    }
}
