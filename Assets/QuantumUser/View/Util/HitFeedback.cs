using NaughtyAttributes;
using PrimeTween;
using Quantum;
using UnityEngine;

namespace QuantumUser.View.Util
{
    public class HitFeedback : CustomQuantumEntityViewComponent
    {
        [SerializeField] private SpriteRenderer[] sprites;

        [Header("Hit Flash")]
        [SerializeField, Tooltip("Used for a Neutral-element hit (plain weapon/skill damage) - everything without a more specific color below.")]
        private Color flashColor = Color.white;
        [SerializeField, Tooltip("Used instead of flashColor when the hit's ElementType is Fire - i.e. a Burn tick (see StatusEffectSystem.TickBurn).")]
        private Color burnFlashColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField, Tooltip("Used instead of flashColor when the hit's ElementType is Poison - i.e. a Poison tick (see StatusEffectSystem.TickPoison).")]
        private Color poisonFlashColor = new Color(1f, 0.4f, 0.7f);
        [SerializeField, Tooltip("Used instead of flashColor/burnFlashColor/poisonFlashColor when EventEntityDamaged.FrontalReduced is true (a FrontalDamageReduction enemy hit within its facing arc) - takes priority over the element color either way.")]
        private Color frontalReducedFlashColor = Color.gray;
        [SerializeField] private Color restColor = Color.clear;
        [SerializeField] private float duration = 0.1f;

        [Header("Heal Flash")]
        [SerializeField, Tooltip("Used on EventEntityHealed - e.g. FlyingShielder healing an ally enemy, or Zara's heal pulse.")]
        private Color healFlashColor = new Color(0.4f, 1f, 0.4f);

        [Header("Death")]
        [SerializeField, Tooltip("Applied the instant the entity dies, and held (hit flash stops overriding it) for the rest of the corpse's lingering duration.")]
        private Color deathColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        private Tween[] _tweens;

        public override void Awake()
        {
            base.Awake();
            _tweens = new Tween[sprites.Length];
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventEntityHealed>(this, OnEntityHealed);
            QuantumEvent.Subscribe<EventEntityDied>(this, OnEntityDied);
            Flash();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnEntityDamaged(EventEntityDamaged e)
        {
            if (e.Target != _entityRef)
                return;

            if (e.Silent == true)
                return;

            if (e.FrontalReduced == true)
            {
                Flash(frontalReducedFlashColor);
                return;
            }

            Flash(e.Element);
        }

        private void OnEntityHealed(EventEntityHealed e)
        {
            if (e.Target != _entityRef)
                return;

            Flash(healFlashColor);
        }

        private void OnEntityDied(EventEntityDied e)
        {
            if (e.Target != _entityRef)
                return;

            Die();
        }

        [Button]
        public void Flash()
        {
            Flash(ElementType.Neutral);
        }

        [Button]
        public void FlashHeal()
        {
            Flash(healFlashColor);
        }

        private void Flash(ElementType element)
        {
            Flash(ResolveFlashColor(element));
        }

        private void Flash(Color color)
        {
            for (var i = 0; i < sprites.Length; i++)
            {
                _tweens[i].Stop();
                sprites[i].color = color;
                _tweens[i] = Tween.Color(sprites[i], color, restColor, duration);
            }
        }

        // Fire/Poison ticks (StatusEffectSystem.TickBurn/TickPoison) carry their element on
        // EntityDamaged - every other hit (plain weapon/skill damage) stays Neutral and keeps the
        // original flashColor.
        private Color ResolveFlashColor(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return burnFlashColor;
                case ElementType.Poison: return poisonFlashColor;
                default: return flashColor;
            }
        }

        // Stops flashing so it can't blink deathColor back to restColor for the rest of the
        // corpse's lingering duration.
        [Button]
        public void Die()
        {
            foreach (var tween in _tweens)
                tween.Stop();

            foreach (var sprite in sprites)
                sprite.color = deathColor;
        }
    }
}
