using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // View for a Breakable prop (see Breakable.qtn). Shows one of two visuals - normal vs. broken -
    // and plays a one-shot break particle at the moment it breaks. Simulation-side the entity is
    // never destroyed on break (only its collider is disabled), so this just swaps which visual root
    // is active.
    //
    // Two paths keep it correct in every case, same split ChestView uses:
    //   - EventBreakableBroken (fired once by BreakableUtility.TryBreak) drives the break PARTICLE -
    //     a pooled, external EffectsManager one-shot so it survives independent of the visual swap.
    //   - QUpdate reconciles which visual root is active to the live Breakable.Broken flag every
    //     frame, so a client that joined AFTER the break (and so never received the event), or a
    //     predicted/rolled-back frame, always shows the right state. QUpdate never plays the
    //     particle - only the event does - so the burst fires exactly once on the break.
    public class BreakableView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Shown while the prop is intact. Disabled on break.")]
        private GameObject normalVisual;
        [SerializeField, Tooltip("Shown once the prop is broken (debris/cracked husk). Disabled while intact.")]
        private GameObject brokenVisual;

        [SerializeField, Tooltip("Played once at the break, as a pooled EXTERNAL one-shot via EffectsManager (not a child of this entity) so it finishes on its own. Leave unassigned to skip. Must be a non-looping prefab.")]
        private ParticleSystem breakEffectPrefab;
        [SerializeField, Tooltip("World-space offset from the prop's position where breakEffectPrefab plays.")]
        private Vector3 breakEffectOffset;

        // Tracks which visual is currently shown so QUpdate only toggles on an actual change, and so
        // the particle (event path) never double-fires against a state QUpdate already settled.
        private bool _shownBroken;

        public override void Awake()
        {
            base.Awake();

            ApplyState(false);

            QuantumEvent.Subscribe<EventBreakableBroken>(this, OnBreakableBroken);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnBreakableBroken(EventBreakableBroken e)
        {
            if (e.Entity != _entityRef)
                return;

            PlayBreakParticle();
            ApplyState(true);
        }

        protected override void QUpdate(QuantumGame game)
        {
            var frame = game.Frames.Predicted;

            if (frame.TryGet<Breakable>(_entityRef, out var breakable) == false)
                return;

            if (breakable.Broken != _shownBroken)
                ApplyState(breakable.Broken);
        }

        private void ApplyState(bool broken)
        {
            _shownBroken = broken;

            if (normalVisual != null)
                normalVisual.SetActive(broken == false);

            if (brokenVisual != null)
                brokenVisual.SetActive(broken == true);
        }

        private void PlayBreakParticle()
        {
            if (breakEffectPrefab != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(breakEffectPrefab, transform.position + breakEffectOffset, Quaternion.identity);
        }
    }
}
