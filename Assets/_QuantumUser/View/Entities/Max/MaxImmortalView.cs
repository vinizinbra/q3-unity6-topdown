using Photon.Deterministic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Max's Last Stand rank 3 ("Too Angry to Die") - view feedback for CheatDeathUtility.
    // TryPreventLethal's save: a one-shot burst the instant it triggers (reacting to the dedicated
    // CheatDeathTriggered event) plus a continuous aura for as long as the post-save immunity window
    // is still open. Both fields are direct child ParticleSystems on Max's own hierarchy, played/
    // stopped in place - same shape BerserkFxView already uses for its own aura fields - rather than
    // routed through EffectsManager's pooled/instantiated PlayEffect, since these are anchored to one
    // specific character rather than a shared effect spawned at an arbitrary world position.
    //
    // Polls StatusEffects.CheatDeathImmunityRemaining directly (the exact field CheatDeathUtility
    // itself sets) rather than the generic Invulnerable tag, so this can never cross-trigger off some
    // unrelated future source of Invulnerable - same "poll a live condition every frame, toggle a
    // persistent aura" idiom BerserkFxView already established for Rage Overdrive.
    //
    // This effect had no way to actually show before now - CheatDeathUtility.TryPreventLethal only
    // opens the immunity window at all when the entity already carries StatusEffects
    // (`f.Unsafe.TryGetPointer<StatusEffects>(entity, ...)`), and Max's own player prototype never
    // had that component until it was added directly to Max.prefab alongside this view. The Health
    // clamp/Overdrive-end half of Too Angry to Die worked regardless, but the Invulnerable window
    // never actually opened - Max would take the followup hit immediately after "surviving" it.
    public class MaxImmortalView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played once, in place, the instant Too Angry to Die actually saves Max. A direct child ParticleSystem, not a spawned prefab. Skipped if left empty.")]
        private ParticleSystem triggerEffect;

        [SerializeField, Tooltip("Played/stopped in place for as long as the post-save immunity window is open. A direct child ParticleSystem. Skipped if left empty.")]
        private ParticleSystem immunityAura;

        private bool _immune;

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventCheatDeathTriggered>(this, OnCheatDeathTriggered);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _immune = false;
            immunityAura?.Stop();
        }

        protected override void QUpdate(QuantumGame game)
        {
            bool immune = IsImmune(game.Frames.Verified, _entityRef);

            if (immune == _immune)
                return;

            _immune = immune;

            if (immunityAura == null)
                return;

            if (immune == true)
                immunityAura.Play();
            else
                immunityAura.Stop();
        }

        private void OnCheatDeathTriggered(EventCheatDeathTriggered e)
        {
            if (e.Entity != _entityRef || triggerEffect == null)
                return;

            triggerEffect.Play();
        }

        private static bool IsImmune(Frame f, EntityRef entity)
        {
            if (f.Has<StatusEffects>(entity) == false)
                return false;

            return f.Get<StatusEffects>(entity).CheatDeathImmunityRemaining > FP._0;
        }
    }
}
