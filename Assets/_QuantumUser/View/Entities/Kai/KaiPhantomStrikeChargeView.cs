using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Kai's Phantom Strike Ascension - a looping particle on Kai himself while his next-shot bonus is
    // ARMED. The one-shot PhantomStrikeCharge tag is added by PhantomStrikeSkillAction's own Dash
    // Begin-phase Execute and removed the instant his next shot fires and consumes it (WeaponSystem),
    // so this just plays while the tag is present and stops the moment it's spent - the same
    // "component present -> looping hero particle" shape as ProtectorAuraView, minus the radius scale
    // and grounded gate (the charge is meaningful mid-dash/airborne too, and it reads as "your next
    // shot is loaded" rather than a ground zone).
    public class KaiPhantomStrikeChargeView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played while Kai holds a Phantom Strike charge (his next shot is buffed) - stopped/cleared the instant that shot fires and consumes it. Left unassigned to skip.")]
        private ParticleSystem chargeParticle;

        private bool _armed;

        public override void Awake()
        {
            base.Awake();

            if (chargeParticle != null)
                chargeParticle.gameObject.SetActive(false);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (chargeParticle == null)
                return;

            bool armed = game.Frames.Predicted.Has<PhantomStrikeCharge>(_entityRef);

            if (armed == _armed)
                return;

            _armed = armed;

            if (armed)
            {
                chargeParticle.gameObject.SetActive(true);
                chargeParticle.Play(true);
            }
            else
            {
                chargeParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                chargeParticle.gameObject.SetActive(false);
            }
        }
    }
}
