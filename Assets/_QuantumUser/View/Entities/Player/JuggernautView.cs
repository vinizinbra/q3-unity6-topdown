using System;
using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Same polling shape as BerserkFxView (checked directly against CharacterSkills/JuggernautCharge
    // each frame rather than via a QuantumEvent pair, since these are continuous states for the
    // whole activation, not one-shot occurrences) - generalized into a data-driven per-state list
    // instead of hardcoded per-state fields, so adding/retuning a state doesn't need new fields.
    // Sprites are captured on Initialize and auto-restored whenever the state resolves back to
    // Initial, so Initial needs no authored SpriteSwaps of its own - there's no way for it to drift
    // from whatever the prefab actually looks like at rest.
    public class JuggernautView : CustomQuantumEntityViewComponent
    {
        private enum JuggernautVisualState
        {
            Initial, // Juggernaut not active
            Active,  // active, not yet Charged
            Charged, // active and Charged - see JuggernautCharge.ChargePoints vs JuggernautSkillData.MaxCharge
        }

        [Serializable]
        private class SpriteSwap
        {
            public SpriteRenderer Target;
            public Sprite Sprite;
        }

        [Serializable]
        private class StateVisual
        {
            public JuggernautVisualState State;
            public List<SpriteSwap> SpriteSwaps = new();
            public List<ParticleSystem> Particles = new();
            public List<TrailRenderer> Trails = new();
        }

        [SerializeField] private List<StateVisual> stateVisuals = new();
        [SerializeField] private ParticleSystem stateChangedParticle;
        [SerializeField] private ParticleSystem chargedGroundedParticle;
        [SerializeField] private CharView charView;
        [SerializeField] private BlobAnimationView blobAnimationView;
        [SerializeField, Tooltip("Multiplies BlobAnimationView's forward run lean while Active or Charged (i.e. state != Initial) - reverts to 1x the instant the skill resolves back to Initial.")]
        private float chargedRunLeanForwardMultiplier = 2f;

        private readonly Dictionary<SpriteRenderer, Sprite> _originalSprites = new();

        private JuggernautVisualState _currentState;
        private bool _initialized;
        private bool _grounded;

        public override void Awake()
        {
            base.Awake();
            charView = GetComponentInParent<CharView>();
            blobAnimationView = GetComponentInParent<BlobAnimationView>();
        }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            CaptureOriginalSprites();
            StopAllParticles();
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _initialized = false;
            Apply(JuggernautVisualState.Initial);
            StopGroundedParticle();
            UpdateRunLean(JuggernautVisualState.Initial);
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Verified;
            JuggernautVisualState state = ResolveState(f, _entityRef);

            if (_initialized == false || state != _currentState)
            {
                bool isTransition = _initialized == true;
                _initialized = true;
                _currentState = state;
                Apply(state);

                if (isTransition == true)
                    PlayStateChangedParticle();
            }

            UpdateGroundedParticle();
            UpdateRunLean(state);
        }

        // Forces every particle system this component drives (per-state, state-changed, grounded)
        // off and cleared at spawn - stops whatever the prefab's own Play On Awake/editor preview
        // left them at, so Apply's per-state play/stop starts from a known-clean baseline instead
        // of inheriting stray emission from before the state machine took over.
        private void StopAllParticles()
        {
            foreach (var stateVisual in stateVisuals)
            {
                foreach (var particle in stateVisual.Particles)
                {
                    if (particle != null)
                        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            if (stateChangedParticle != null)
                stateChangedParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (chargedGroundedParticle != null)
                chargedGroundedParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void CaptureOriginalSprites()
        {
            foreach (var stateVisual in stateVisuals)
            {
                foreach (var swap in stateVisual.SpriteSwaps)
                {
                    if (swap.Target != null && _originalSprites.ContainsKey(swap.Target) == false)
                        _originalSprites[swap.Target] = swap.Target.sprite;
                }
            }
        }

        private void Apply(JuggernautVisualState state)
        {
            StateVisual active = null;

            foreach (var stateVisual in stateVisuals)
            {
                bool isTarget = stateVisual.State == state;

                if (isTarget == true)
                    active = stateVisual;

                foreach (var particle in stateVisual.Particles)
                {
                    if (particle == null)
                        continue;

                    if (isTarget == true)
                        particle.Play();
                    else
                        particle.Stop();
                }

                foreach (var trail in stateVisual.Trails)
                {
                    if (trail != null)
                        trail.emitting = isTarget;
                }
            }

            if (state == JuggernautVisualState.Initial)
            {
                RestoreOriginalSprites();
                return;
            }

            if (active == null)
                return;

            foreach (var swap in active.SpriteSwaps)
            {
                if (swap.Target != null && swap.Sprite != null)
                    swap.Target.sprite = swap.Sprite;
            }
        }

        private void RestoreOriginalSprites()
        {
            foreach (var kvp in _originalSprites)
            {
                if (kvp.Key != null)
                    kvp.Key.sprite = kvp.Value;
            }
        }

        private void PlayStateChangedParticle()
        {
            if (stateChangedParticle != null)
                stateChangedParticle.Play();
        }

        // Reads CharView.LocalIsGrounded rather than raycasting or checking KCC.Data.IsGrounded
        // directly - same reasoning as RunDustFxView: CharView already computes this once per
        // character per frame, so every ground-gated view component shares that one view-layer
        // ground truth instead of each doing its own check. Gated on Charged too so this only
        // reads as "max charge, planted on the ground" - it turns off the instant either the
        // charge drops or the character leaves the ground (e.g. mid-launch).
        private void UpdateGroundedParticle()
        {
            bool grounded = charView.LocalIsGrounded && _currentState == JuggernautVisualState.Charged;
            if (grounded == _grounded)
                return;

            _grounded = grounded;

            if (grounded == true)
                PlayGroundedParticle();
            else
                StopGroundedParticle();
        }

        // Only at full Charged, not merely Active (still building charge) - reverts to 1x the
        // instant it drops back out of Charged. BlobAnimationView's own _leanT lerp
        // (leanLerpSpeed) already smooths the transition on both ends, so this just needs to hand
        // it the right target every frame rather than easing anything itself.
        private void UpdateRunLean(JuggernautVisualState state)
        {
            if (blobAnimationView == null)
                return;

            blobAnimationView.RunLeanForwardMultiplier = state == JuggernautVisualState.Charged ? chargedRunLeanForwardMultiplier : 1f;
        }

        [Button]
        private void PlayGroundedParticle()
        {
            if (chargedGroundedParticle != null)
                chargedGroundedParticle.Play();
        }

        [Button]
        private void StopGroundedParticle()
        {
            if (chargedGroundedParticle != null)
                chargedGroundedParticle.Stop();
        }

        [Button("Preview Initial")]
        private void PreviewInitial() => Apply(JuggernautVisualState.Initial);

        [Button("Preview Active")]
        private void PreviewActive() => Apply(JuggernautVisualState.Active);

        [Button("Preview Charged")]
        private void PreviewCharged() => Apply(JuggernautVisualState.Charged);

        [Button("Preview State Changed Particle")]
        private void PreviewStateChangedParticle() => PlayStateChangedParticle();

        private static JuggernautVisualState ResolveState(Frame f, EntityRef entity)
        {
            if (f.Has<CharacterSkills>(entity) == false)
                return JuggernautVisualState.Initial;

            CharacterSkills skills = f.Get<CharacterSkills>(entity);
            JuggernautSkillData skill = ResolveActiveSkill(f, skills);

            if (skill == null)
                return JuggernautVisualState.Initial;

            if (f.Has<JuggernautCharge>(entity) == false)
                return JuggernautVisualState.Active;

            JuggernautCharge charge = f.Get<JuggernautCharge>(entity);
            return charge.ChargePoints >= skill.MaxCharge ? JuggernautVisualState.Charged : JuggernautVisualState.Active;
        }

        // Checks both skill slots and the resolved asset's own type rather than a fixed slot index,
        // same reasoning as BerserkFxView.IsSlotActive - which slot ends up carrying
        // JuggernautSkillData is per-hero prototype config, not guaranteed.
        private static JuggernautSkillData ResolveActiveSkill(Frame f, CharacterSkills skills)
        {
            if (TryResolveActiveSkill(f, skills.DashSkill, out var skill) == true)
                return skill;

            if (TryResolveActiveSkill(f, skills.HeroSkill, out skill) == true)
                return skill;

            return null;
        }

        private static bool TryResolveActiveSkill(Frame f, SkillSlot slot, out JuggernautSkillData skill)
        {
            skill = null;

            if (slot.State != SkillState.Active)
                return false;

            skill = f.FindAsset(slot.Skill) as JuggernautSkillData;
            return skill != null;
        }
    }
}
