using NaughtyAttributes;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Same polling shape as BerserkFxView/RunDustFxView - checked directly against
    // CharacterSkills.DashSkill.State each frame rather than via a QuantumEvent pair, since this is
    // a continuous state for the whole dash, not a one-shot occurrence. Reads DashSkill specifically
    // (not both slots like BerserkFxView/JuggernautView) - Dash is a fixed, dedicated slot per
    // CharacterSkills.qtn, never shared with HeroSkill.
    public class DashFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Motion-streak trail, emitting only while the dash is Active.")]
        private TrailRenderer trail;

        [SerializeField, Tooltip("Shared player FX config - DashBurst is played (via EffectsManager, tinted with the hero's RingColor) each time the dash begins.")]
        private PlayerFxConfig fxConfig;

        private bool _active;
        private Color _ringColor = Color.white;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            ApplyRingColorTint(game.Frames.Verified);
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _active = false;
            Stop();
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Verified;
            bool active = IsDashActive(f, _entityRef);

            if (active == _active)
                return;

            _active = active;

            if (active == true)
                Play();
            else
                Stop();
        }

        [Button]
        private void Play()
        {
            if (trail != null)
                trail.emitting = true;

            if (fxConfig != null && fxConfig.DashBurst.Prefab != null && EffectsManager.Instance != null)
            {
                Vector3 scale = fxConfig.DashBurst.Prefab.transform.localScale * fxConfig.DashBurst.ScaleMultiplier;
                Vector3 position = ResolveCenterPosition() + fxConfig.DashBurst.ResolveWorldPositionOffset(transform);
                EffectsManager.Instance.PlayEffect(fxConfig.DashBurst.Prefab, position, transform.rotation, scale, _ringColor);
            }
        }

        // Dash burst spawns at the player's center (torso height) rather than feet/ground level
        // like Jump/Grounded's bursts. EnemyMovementUtility.ResolveEntityCenter is a general
        // Quantum-side "entity center" helper (KCC.Position + Height/2 for any KCC-driven entity,
        // i.e. players) despite living in an enemy-named file - already reused for non-enemy
        // entities elsewhere (see EffectsManager). Falls back to transform.position if no frame is
        // available yet (e.g. the Play() test button pressed outside Play mode).
        private Vector3 ResolveCenterPosition()
        {
            Frame f = _game != null ? _game.Frames.Verified : null;
            if (f == null)
                return transform.position;

            return EnemyMovementUtility.ResolveEntityCenter(f, _entityRef).ToUnityVector3();
        }

        [Button]
        private void Stop()
        {
            if (trail != null)
                trail.emitting = false;
        }

        private static bool IsDashActive(Frame f, EntityRef entity)
        {
            if (f.Has<CharacterSkills>(entity) == false)
                return false;

            return f.Get<CharacterSkills>(entity).DashSkill.State == SkillState.Active;
        }

        // Resolves MovementRingView's per-hero RingColor (see CharacterData.RingColor) and caches
        // it for the burst played from Play(), plus tints the trail to match instead of the flat
        // white authored on the prefab - keeps the fade-in/out alpha keys as originally authored,
        // only the RGB is swapped.
        private void ApplyRingColorTint(Frame frame)
        {
            if (frame.Has<CharacterStats>(_entityRef) == false)
                return;

            CharacterData data = frame.FindAsset(frame.Get<CharacterStats>(_entityRef).CharacterData);
            if (data == null)
                return;

            _ringColor = data.RingColor;

            if (trail == null)
                return;

            Gradient gradient = trail.colorGradient;
            GradientColorKey[] colorKeys = gradient.colorKeys;
            for (int i = 0; i < colorKeys.Length; i++)
                colorKeys[i].color = _ringColor;

            gradient.SetKeys(colorKeys, gradient.alphaKeys);
            trail.colorGradient = gradient;
        }
    }
}
