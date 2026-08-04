using System.Collections.Generic;
using NaughtyAttributes;
using PrimeTween;
using Quantum;
using UnityEngine;

namespace QuantumUser.View.Util
{
    public class HitFeedback : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Inspector-wired directly on prefabs where this component lives alongside its own SpriteRenderers (player characters, Sentry). Enemies leave this empty and wire it instead through SetRig - see that method's comment.")]
        private SpriteRenderer[] sprites;

        [Header("Hit Flash")]
        [SerializeField, Tooltip("Used for a Neutral-element hit (plain weapon/skill damage) - everything without a more specific color below.")]
        private Color flashColor = Color.white;
        [SerializeField, Tooltip("Used instead of flashColor when the hit's ElementType is Fire - i.e. a Burn tick (see StatusEffectSystem.TickBurn).")]
        private Color burnFlashColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField, Tooltip("Used instead of flashColor/burnFlashColor when EventEntityDamaged.FrontalReduced is true (a FrontalDamageReduction enemy hit within its facing arc) - takes priority over the element color either way.")]
        private Color frontalReducedFlashColor = Color.gray;
        [SerializeField] private Color restColor = Color.clear;
        [SerializeField] private float duration = 0.1f;

        [Header("Heal Flash")]
        [SerializeField, Tooltip("Used on EventEntityHealed - e.g. FlyingShielder healing an ally enemy, or Zara's heal pulse.")]
        private Color healFlashColor = new Color(0.4f, 1f, 0.4f);

        [Header("Shield Flash")]
        [SerializeField, Tooltip("Used on EventEntityShielded - e.g. Bodyguard/Portable Cover granting Shield.")]
        private Color shieldFlashColor = new Color(0.4f, 0.75f, 1f);

        [Header("Death")]
        [SerializeField, Tooltip("Applied the instant the entity dies, and held (hit flash stops overriding it) for the rest of the corpse's lingering duration.")]
        private Color deathColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        [Header("XP Pickup")]
        [SerializeField, Tooltip("Used on EventExpOrbCollected, only for whichever character actually touched the orb (e.Collector == _entityRef) - the exp itself credits the whole co-op run regardless (see ExperienceUtility.Grant), this is purely 'it was me who grabbed it' feedback.")]
        private Color pickupGlowColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField, Tooltip("Longer than the snappy hit-flash duration - reads as a glow rather than a flash.")]
        private float pickupGlowDuration = 0.5f;

        [Header("Rift Mark")]
        [SerializeField, Tooltip("StatusEffects.RiftMarkStacks - see StatusEffectUtility.IsRiftMarked. Sprites swap to this material (pink inner glow via Sprites/Sprite Status Colorise Flash's _GlowColor/_GlowIntensity) while active, and back to their own original material otherwise. Hit-flash is untouched by this - it still writes SpriteRenderer.color same as always, on top of whichever material is currently assigned.")]
        private Material riftMarkMaterial;
        [SerializeField, Tooltip("Brief, subtle tint played only when RiftMarkStacks actually increases (never on a refresh-only reapply at max stacks, and never a full material swap) - deliberately quiet, distinct from the stronger per-reaction flash colors below. Hot-pink #FD3971 per this project's Rift Mark color rule (see docs/elemental-reactions.md) - purple stays reserved for Void.")]
        private Color riftMarkApplicationFlashColor = new Color32(0xFD, 0x39, 0x71, 0xFF);
        [SerializeField] private float riftMarkApplicationFlashDuration = 0.08f;

        [Header("Freeze Mark")]
        [SerializeField, Tooltip("StatusEffects.AnticipationSlowRemaining - see StatusEffectUtility.IsAnticipationSlowed (Ice+RiftMark's Deep Freeze reaction, docs/elemental-reactions.md). Takes priority over riftMarkMaterial when both are active on the same target.")]
        private Material freezeMaterial;

        private enum MarkState { Normal, Rift, Freeze }

        // Keyed by SpriteRenderer instance (not by this component) because the renderers live on
        // the pooled ViewPrefab (see ViewPrefabPool) while HitFeedback itself is a fresh instance
        // per enemy spawn - a plain instance field would just re-trust whatever material the
        // previous pool occupant happened to leave assigned. Baking each renderer's true original
        // once, the first time it's ever seen, means InitializeSprites can always force-restore to
        // it below instead of reading (possibly leaked) current state.
        private static readonly Dictionary<SpriteRenderer, Material> _bakedOriginalMaterials = new Dictionary<SpriteRenderer, Material>();

        private Material[] _originalMaterials;
        private MarkState _markState;
        private byte _lastRiftMarkStacks;
        private bool _dead;

        private Tween[] _tweens;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventEntityHealed>(this, OnEntityHealed);
            QuantumEvent.Subscribe<EventEntityShielded>(this, OnEntityShielded);
            QuantumEvent.Subscribe<EventEntityDied>(this, OnEntityDied);
            QuantumEvent.Subscribe<EventExpOrbCollected>(this, OnExpOrbCollected);

            // Enemies leave sprites empty here and populate it later via SetRig instead - see that
            // method's comment for why.
            if (sprites != null && sprites.Length > 0)
                InitializeSprites();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        // Called by EnemyView.ConnectRig right after EnemyView.SpawnSprite instantiates the enemy-
        // type ViewPrefab - this component now lives on the generic enemy prototype (alongside
        // EnemyView itself) so that CustomQuantumEntityViewComponent.Awake's QuantumEntityView
        // lookup succeeds immediately (the pooled ViewPrefab used to host this directly, and was
        // still unparented when its own Awake ran, so the lookup always failed and _entityRef was
        // never set - see EnemyBlobAnimationView/EnemyArmAimView.SetRig for the same pattern).
        // sprites is empty until this runs, so the tween-array/initial-flash setup has to happen
        // here instead of Awake for enemies specifically.
        public void SetRig(EnemyViewRig rig)
        {
            sprites = rig.Sprites;
            InitializeSprites();
        }

        private void InitializeSprites()
        {
            _tweens = new Tween[sprites.Length];
            _dead = false;
            _markState = MarkState.Normal;
            _lastRiftMarkStacks = 0;

            // Force-restores every sprite to its baked-original material on spawn - not just a
            // read, a write - so a pooled ViewPrefab that got recycled while still Rift-Marked/
            // Frozen (see Die() below) never shows the leftover material on the enemy now
            // occupying it.
            _originalMaterials = new Material[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                if (_bakedOriginalMaterials.TryGetValue(sprites[i], out Material original) == false)
                {
                    original = sprites[i].sharedMaterial;
                    _bakedOriginalMaterials[sprites[i]] = original;
                }

                _originalMaterials[i] = original;
                sprites[i].sharedMaterial = original;
            }

            Flash();
        }

        // Rift/Freeze are material swaps, not tweens - each status is either active or it isn't,
        // so sprites just jump to the matching material and back rather than easing a color.
        // Deep Freeze (Ice+RiftMark's reaction) takes priority over a plain Rift Mark when both are active.
        protected override void QUpdate(QuantumGame game)
        {
            if (_dead || sprites == null)
                return;

            Frame frame = game.Frames.Predicted;
            if (frame == null)
                return;

            // Subtle application flash - fires only on an actual stack-count increase (0->1, 1->2),
            // never on a refresh-only reapply at max stacks and never as a full material swap. Reads
            // before the MarkState swap below so it can compare against the previous frame's stacks
            // independent of whichever material state currently applies.
            byte stacks = StatusEffectUtility.GetRiftMarkStacks(frame, _entityRef);
            if (stacks > _lastRiftMarkStacks)
                Flash(riftMarkApplicationFlashColor, riftMarkApplicationFlashDuration);
            _lastRiftMarkStacks = stacks;

            MarkState state = MarkState.Normal;
            if (freezeMaterial != null && StatusEffectUtility.IsAnticipationSlowed(frame, _entityRef))
                state = MarkState.Freeze;
            else if (riftMarkMaterial != null && StatusEffectUtility.IsRiftMarked(frame, _entityRef))
                state = MarkState.Rift;

            if (state == _markState)
                return;

            _markState = state;
            Material material = state == MarkState.Freeze ? freezeMaterial : state == MarkState.Rift ? riftMarkMaterial : null;
            for (var i = 0; i < sprites.Length; i++)
                sprites[i].sharedMaterial = material != null ? material : _originalMaterials[i];
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

        private void OnEntityShielded(EventEntityShielded e)
        {
            if (e.Target != _entityRef)
                return;

            Flash(shieldFlashColor);
        }

        private void OnEntityDied(EventEntityDied e)
        {
            if (e.Target != _entityRef)
                return;

            Die();
        }

        private void OnExpOrbCollected(EventExpOrbCollected e)
        {
            if (e.Collector != _entityRef)
                return;

            Flash(pickupGlowColor, pickupGlowDuration);
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

        private void Flash(Color color) => Flash(color, duration);

        private void Flash(Color color, float flashDuration)
        {
            if (sprites == null)
                return;

            for (var i = 0; i < sprites.Length; i++)
            {
                _tweens[i].Stop();
                sprites[i].color = color;
                _tweens[i] = Tween.Color(sprites[i], color, restColor, flashDuration);
            }
        }

        // Burn ticks (StatusEffectSystem.TickBurn) carry their element on EntityDamaged - every
        // other hit (plain weapon/skill damage) stays Neutral and keeps the original flashColor.
        private Color ResolveFlashColor(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return burnFlashColor;
                default: return flashColor;
            }
        }

        // Stops flashing so it can't blink deathColor back to restColor for the rest of the
        // corpse's lingering duration. Also force-restores the original material immediately -
        // without this a corpse that died while Rift-Marked/Frozen would linger showing that
        // material under the death tint, and (since QUpdate bails out once _dead is true, so
        // nothing else would ever swap it back) the pooled ViewPrefab would still have it assigned
        // when released back to ViewPrefabPool for the next enemy to inherit - see
        // InitializeSprites' _bakedOriginalMaterials for the other half of that fix.
        [Button]
        public void Die()
        {
            if (_tweens == null)
                return;

            _dead = true;
            _markState = MarkState.Normal;

            foreach (var tween in _tweens)
                tween.Stop();

            for (var i = 0; i < sprites.Length; i++)
            {
                sprites[i].color = deathColor;
                sprites[i].sharedMaterial = _originalMaterials[i];
            }
        }
    }
}
