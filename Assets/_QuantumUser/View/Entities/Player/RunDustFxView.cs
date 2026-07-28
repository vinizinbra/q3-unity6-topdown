using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Ground dust for as long as the character is grounded, off the instant it leaves the
    // ground - no speed/state gating beyond that, unlike BlobAnimationView's fuller
    // Idle/Run/Air state machine. dust is expected to be parented under a SnapToGround so it
    // plays at floor level rather than following the rig's own bob/squash.
    //
    // Reads CharView.LocalIsGrounded rather than doing its own Physics.Raycast or checking
    // KCC.Data.IsGrounded - CharView already computes this once per character per frame, so any
    // other view component that needs the same view-layer ground truth (e.g. footstep sounds
    // later) can just watch it too instead of each raycasting independently.
    public class RunDustFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField] private ParticleSystem dust;
        [SerializeField] private CharView charView;

        private bool _grounded;

        public override void Awake()
        {
            base.Awake();
            charView = GetComponentInParent<CharView>();
        }
        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            Stop();
        }

        protected override void QUpdate(QuantumGame game)
        {
            bool grounded = charView.LocalIsGrounded;
            if (grounded == _grounded)
                return;

            _grounded = grounded;

            if (grounded == true)
                Play();
            else
                Stop();
        }

        [Button]
        private void Play() => dust.Play();

        [Button]
        private void Stop() => dust.Stop();
    }
}
