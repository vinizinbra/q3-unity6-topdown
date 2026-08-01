using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Events;

namespace Quantum
{
    // Fully generic tick relay - carries no ParticleSystem/effect knowledge of its own. OnIdle
    // fires once the area's entity is instantiated; OnTick fires every AreaDamageSystem tick
    // (AreaDamageTicked), whether or not that tick actually caught a target. Wire whatever a given
    // area needs (particles, sound, camera shake) onto these from the Inspector.
    public class AreaHitView : CustomQuantumEntityViewComponent
    {
        [SerializeField] private UnityEvent onIdle;
        [SerializeField] private UnityEvent onTick;

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventAreaDamageTicked>(this, OnAreaDamageTicked);
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            onIdle?.Invoke();
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnAreaDamageTicked(EventAreaDamageTicked e)
        {
            if (e.Entity != _entityRef)
                return;

            onTick?.Invoke();
        }
    }
}
