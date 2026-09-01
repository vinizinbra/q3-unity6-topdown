using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Lightweight hit reaction for any Breakable prop (see Breakable.qtn/BreakableView) - a color
    // blink plus an optional punch-scale on every non-killing hit, so a barrel/crate visibly reacts
    // before it actually breaks. Deliberately NOT HitFeedback (Assets/_QuantumUser/View/Util/
    // HitFeedback.cs) - that component is built for characters (Rift Mark/Freeze material swaps,
    // heal/shield/pickup/revive flashes, a permanent death tint) and none of that applies to a
    // static prop. This is the prop-sized subset: flash + punch only, reusable on any Breakable.
    public class BreakableHitFeedback : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Flashed on every hit. Wire both the normal and broken sprite even though only one is ever active at a time - tweening an inactive renderer's color is harmless, and this way the component doesn't need to know which BreakableView is currently showing.")]
        private SpriteRenderer[] sprites;

        [SerializeField, Tooltip("Deliberately overbright (>1) so it reads as a flash-to-white regardless of the sprite's own base color, without needing a dedicated flash shader/material swap.")]
        private Color flashColor = new Color(2.5f, 2.5f, 2.5f, 1f);
        [SerializeField, Tooltip("Should match the sprite's authored tint - plain white/opaque for an unmodified sprite.")]
        private Color restColor = Color.white;
        [SerializeField] private float flashDuration = 0.1f;

        [SerializeField, Tooltip("Punch-scaled on every hit - wire the prop's own JuicyEffects (usually on the same root, since a Breakable's normal/broken visuals are siblings that should scale together). Optional: leave unassigned to skip the scale punch and only flash.")]
        private JuicyEffects juicyEffects;

        private Tween[] _tweens;

        public override void Awake()
        {
            base.Awake();

            _tweens = new Tween[sprites.Length];
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        // No per-frame work - this component is purely event-driven (OnEntityDamaged below).
        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnEntityDamaged(EventEntityDamaged e)
        {
            if (e.Target != _entityRef || e.Silent == true)
                return;

            if (juicyEffects != null)
                juicyEffects.PlayPunchScale();

            for (var i = 0; i < sprites.Length; i++)
            {
                _tweens[i].Stop();
                sprites[i].color = flashColor;
                _tweens[i] = Tween.Color(sprites[i], flashColor, restColor, flashDuration);
            }
        }
    }
}
