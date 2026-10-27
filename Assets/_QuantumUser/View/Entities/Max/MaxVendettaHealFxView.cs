using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Max's Vendetta passive - one-shot VFX played on Max's own view the instant he heals from
    // consuming a mark (killing one of his RevengeMark-marked enemies). Reacts to the dedicated
    // VendettaRevengeHealed event (not the generic EntityHealed - see that event's own comment in
    // Events.qtn) so this never fires off an unrelated heal source (regen, a support ally, etc).
    // Played at this entity's own current view-transform position rather than resolving Transform3D
    // off the frame, since it's always meant to appear on Max himself, not at a world position
    // carried by the event. See docs/max-vendetta-fire-mastery.md.
    // Also plays a one-shot "marked" VFX on each enemy this Max newly marks (VendettaMarkApplied),
    // at the position the event carries, since the enemy's own view isn't this component's.
    public class MaxVendettaHealFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played once whenever Max heals from consuming a Vendetta mark. Skipped if left empty.")]
        private ParticleSystem healEffectPrefab;

        [SerializeField, Tooltip("Played once on an enemy whenever this Max newly marks it with Vendetta. Skipped if left empty.")]
        private ParticleSystem markEffectPrefab;

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventVendettaRevengeHealed>(this, OnVendettaRevengeHealed);
            QuantumEvent.Subscribe<EventVendettaMarkApplied>(this, OnVendettaMarkApplied);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnVendettaRevengeHealed(EventVendettaRevengeHealed e)
        {
            if (e.Entity != _entityRef || healEffectPrefab == null || EffectsManager.Instance == null)
                return;

            EffectsManager.Instance.PlayEffect(healEffectPrefab, transform.position, Quaternion.identity);
        }

        private void OnVendettaMarkApplied(EventVendettaMarkApplied e)
        {
            if (e.Owner != _entityRef || markEffectPrefab == null || EffectsManager.Instance == null)
                return;

            EffectsManager.Instance.PlayEffect(markEffectPrefab, e.Position.ToUnityVector3(), Quaternion.identity);
        }
    }
}
