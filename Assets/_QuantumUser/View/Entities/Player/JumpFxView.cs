using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // One-shot burst on the character's jump events - sibling to BlobAnimationView, which drives
    // the anticipation squash/flip/sound off the same two events. Note "jump" in this game is the
    // auto-mantle ledge assist (AutoJumpSystem / EventPlayerJumped) plus the auto-hop-down variant
    // (EventPlayerAutoJumpedDown) - there is no manual jump input.
    public class JumpFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Shared player FX config - JumpBurst is played on EventPlayerJumped/EventPlayerAutoJumpedDown.")]
        private PlayerFxConfig fxConfig;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventPlayerJumped>(this, OnPlayerJumped);
            QuantumEvent.Subscribe<EventPlayerAutoJumpedDown>(this, OnPlayerAutoJumpedDown);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnPlayerJumped(EventPlayerJumped e)
        {
            if (e.Entity != _entityRef) return;
            Play();
        }

        private void OnPlayerAutoJumpedDown(EventPlayerAutoJumpedDown e)
        {
            if (e.Entity != _entityRef) return;
            Play();
        }

        private void Play()
        {
            if (fxConfig == null || fxConfig.JumpBurst.Prefab == null || EffectsManager.Instance == null)
                return;

            Quaternion rotation = transform.rotation * Quaternion.Euler(fxConfig.JumpBurst.RotationOffset);
            Vector3 scale = fxConfig.JumpBurst.Prefab.transform.localScale * fxConfig.JumpBurst.ScaleMultiplier;
            Vector3 position = transform.position + fxConfig.JumpBurst.ResolveWorldPositionOffset(transform);
            EffectsManager.Instance.PlayEffect(fxConfig.JumpBurst.Prefab, position, rotation, scale);
        }
    }
}
