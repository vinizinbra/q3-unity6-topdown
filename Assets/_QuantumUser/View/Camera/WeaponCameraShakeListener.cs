using NaughtyAttributes;
using Quantum;
using UnityEngine;

namespace QuantumUser.View
{
    // Shakes FollowCamera on every EventPlayerFired raised by a local player's own weapon - other
    // players' (and enemies'/sentries') shots are ignored, same "local entity" filter
    // DamageFeedbackManager uses for damage numbers. Tier comes from the shooter's currently equipped
    // WeaponDataAsset.ShakeTier, resolved off the Predicted frame (event's payload is just EntityRef).
    public class WeaponCameraShakeListener : QuantumGlobalMonoBehaviour
    {
        [SerializeField][Expandable] private CameraShakeConfig shakeConfig;

        public override void QStart(QuantumGame game)
        {
            QuantumEvent.Subscribe<EventPlayerFired>(this, OnPlayerFired);
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

        private void OnPlayerFired(EventPlayerFired e)
        {
            if (MyLocalPlayer.Instance == null || MyLocalPlayer.Instance.AnyLocalPlayerSetup == false)
                return;

            if (MyLocalPlayer.Instance.IsLocalEntity(e.Entity) == false)
                return;

            Frame frame = e.Game.Frames.Predicted;
            if (frame.TryGet<Weapon>(e.Entity, out var weapon) == false)
                return;

            WeaponDataAsset weaponData = frame.FindAsset(weapon.WeaponData);
            if (weaponData == null || shakeConfig == null)
                return;

            shakeConfig.GetShake(weaponData.ShakeTier, out float amplitude, out float duration, out float frequency);
            FollowCamera.I.Shake(amplitude, duration, frequency);
        }

        [Button("Test Small")]
        public void TestShakeSmall() => TestShake(WeaponShakeTier.Small);

        [Button("Test Medium")]
        public void TestShakeMedium() => TestShake(WeaponShakeTier.Medium);

        [Button("Test Strong")]
        public void TestShakeStrong() => TestShake(WeaponShakeTier.Strong);

        private void TestShake(WeaponShakeTier tier)
        {
            if (shakeConfig == null || FollowCamera.I == null)
                return;

            shakeConfig.GetShake(tier, out float amplitude, out float duration, out float frequency);
            FollowCamera.I.Shake(amplitude, duration, frequency);
        }
    }
}
