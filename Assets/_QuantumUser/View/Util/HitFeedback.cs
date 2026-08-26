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
        [SerializeField, Tooltip("Flash colour when the accessory is put back on - recovered off the ground, or repaired/replaced at the Merchant (EventAccessoryRecovered/EventAccessoryRestored). Low priority, so it never stomps a hit flash.")]
        private Color recoverFlashColor = Color.cyan;
        [SerializeField, Tooltip("Flash colour when the Accessory Guard eats a hit entirely (EventAccessoryBlocked). BLUE, not a damage colour: nothing was actually lost, so it must never read as being hurt. Matches EffectsManager.accessoryBlockedEffectColor so the character flash and the impact spark speak the same language. See docs/accessory-guard.md.")]
        private Color blockFlashColor = new Color(0.25f, 0.6f, 1f);

        [SerializeField, Tooltip("Flash colour when a Free Hit Guard is spent (EventFreeHitGuardConsumed - Brute's Bodyguard today). CYAN: same cool family as the accessory block above (both are negations, neither is damage) but a distinct hue, so which one just saved you is readable at a glance. Matches EffectsManager.freeHitGuardEffectColor.")]
        private Color freeHitGuardFlashColor = new Color(0.4f, 0.95f, 1f);
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

        [Header("Rift Mark")]
        [SerializeField, Tooltip("StatusEffects.RiftMarkStacks - see StatusEffectUtility.IsRiftMarked. Sprites swap to this material (pink inner glow via Sprites/Sprite Status Colorise Flash's _GlowColor/_GlowIntensity) while active, and back to their own original material otherwise. Hit-flash is untouched by this - it still writes SpriteRenderer.color same as always, on top of whichever material is currently assigned.")]
        private Material riftMarkMaterial;
        [SerializeField, Tooltip("Brief, subtle tint played only when RiftMarkStacks actually increases (never on a refresh-only reapply at max stacks, and never a full material swap) - deliberately quiet, distinct from the stronger per-reaction flash colors below. Hot-pink #FD3971 per this project's Rift Mark color rule (see docs/elemental-reactions.md) - purple stays reserved for Void.")]
        private Color riftMarkApplicationFlashColor = new Color32(0xFD, 0x39, 0x71, 0xFF);
        [SerializeField] private float riftMarkApplicationFlashDuration = 0.08f;

        [Header("Pickup Flash")]
        [SerializeField, Tooltip("Used on FlashPickup for CurrencyType.Experience - see FlyingCurrencyManager.OnArrived.")]
        private Color expPickupFlashColor = new Color(0.3f, 0.55f, 1f);
        [SerializeField, Tooltip("Used on FlashPickup for CurrencyType.Coin.")]
        private Color coinPickupFlashColor = new Color(1f, 0.84f, 0.2f);
        [SerializeField, Tooltip("Used on FlashPickup for CurrencyType.RiftShard.")]
        private Color riftShardPickupFlashColor = new Color(1f, 0.35f, 0.75f);
        [SerializeField] private float pickupFlashDuration = 0.35f;

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

        // Timestamp (Time.time, matching Flash's own scaled-time Tween.Color duration) until which
        // FlashPickup below refuses to apply - set by every "important" reaction that goes through
        // the private Flash(Color, float) (hit/heal/shield/rift mark/spawn) OR FlashDamage, so a
        // lower-priority pickup glow (see FlyingCurrencyManager) can never visually stomp a more
        // important flash already playing. No such priority concept existed before pickups needed
        // to defer to hits.
        private float _priorityFlashLockUntil;

        // Timestamp until which every OTHER flash (heal/shield/rift-mark-application/spawn/pickup)
        // refuses to apply - only FlashDamage sets this. Damage is the single highest-priority hit
        // feedback: it always applies immediately regardless of what's currently playing, and
        // nothing else can interrupt it back out while it's still active - unlike every other
        // "important" flash below, which were previously all equal priority (last caller wins).
        private float _damageFlashLockUntil;

        public override void Awake()
        {
            base.Awake();
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventAccessoryBlocked>(this, OnAccessoryBlocked);
            QuantumEvent.Subscribe<EventFreeHitGuardConsumed>(this, OnFreeHitGuardConsumed);
            QuantumEvent.Subscribe<EventAccessoryRecovered>(this, OnAccessoryRecovered);
            QuantumEvent.Subscribe<EventAccessoryRestored>(this, OnAccessoryRestored);
            QuantumEvent.Subscribe<EventEntityHealed>(this, OnEntityHealed);
            QuantumEvent.Subscribe<EventEntityShielded>(this, OnEntityShielded);
            QuantumEvent.Subscribe<EventEntityDied>(this, OnEntityDied);
            QuantumEvent.Subscribe<EventPlayerRespawned>(this, OnPlayerRespawned);
            QuantumEvent.Subscribe<EventPlayerRevived>(this, OnPlayerRevived);

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

            Color color = e.FrontalReduced == true ? frontalReducedFlashColor : ResolveFlashColor(e.Element);
            FlashDamage(color);
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

        // Only ever fires for a player falling off the level (PlayerFallSystem/FallRespawnUtility -
        // DamageUtility.ApplyDamage's own PlayerLink branch no longer respawns, see docs/revive.md)
        // - undoes Die()'s permanent gray tint/QUpdate lockout, since nothing else would (this
        // entity/view is never recreated the way a fresh enemy spawn would re-run InitializeSprites).
        private void OnPlayerRespawned(EventPlayerRespawned e)
        {
            if (e.Entity != _entityRef)
                return;

            Respawn();
        }

        // A lethal hit fires EntityDied (see OnEntityDied/Die() above) UNCONDITIONALLY, before
        // DamageUtility.ApplyDamage's own PlayerLink branch even checks whether the target is a
        // player going Downed rather than truly dying - so a player going Downed/KO gets the exact
        // same permanent gray death tint an enemy corpse does. PlayerRevived (see docs/revive.md/
        // PlayerLifeStateUtility.Revive) is this path's own "undo it" signal, same Respawn() call
        // OnPlayerRespawned already uses for the fall-recovery case above.
        private void OnPlayerRevived(EventPlayerRevived e)
        {
            if (e.Target != _entityRef)
                return;

            Respawn();
        }

        // A hit fully eaten by the Accessory Guard (see docs/accessory-guard.md) deals no damage at
        // all, so it never reaches EventEntityDamaged - without this a block would land completely
        // silently, which reads as the hit having missed rather than having been stopped. Routed
        // through FlashDamage (the top-priority tier) because a block IS an impact: it should never
        // lose out to a heal/shield/pickup glow happening the same moment.
        private void OnAccessoryBlocked(EventAccessoryBlocked e)
        {
            if (e.Owner != _entityRef)
                return;

            FlashDamage(blockFlashColor);
        }

        // A Free Hit Guard being spent negates the hit exactly the way an accessory block does, and
        // reaches no EventEntityDamaged for the same reason - so it needs its own flash or it reads as
        // a miss. Its OWN colour rather than the accessory's, matching the separate impact VFX: both
        // are negations, but they come from different mechanics and should be tellable apart.
        //
        // Routed through FlashDamage (the top-priority tier) despite not being damage, for the same
        // reason a block is: this is an IMPACT, and it must never lose out to a heal/shield/pickup glow
        // landing in the same moment.
        //
        // Keyed off Target (who was saved), not Source (who granted it): the flash belongs on the
        // character that just survived something, whether that's Brute or the teammate he guarded.
        private void OnFreeHitGuardConsumed(EventFreeHitGuardConsumed e)
        {
            if (e.Target != _entityRef)
                return;

            FlashDamage(freeHitGuardFlashColor);
        }

        // Putting the accessory back on - by walking over it, or by paying the Merchant. A LOW
        // priority flash (the same tier a currency pickup uses), deliberately: getting your guard
        // back is good news, and it must never stomp a hit flash that lands in the same moment.
        private void OnAccessoryRecovered(EventAccessoryRecovered e)
        {
            if (e.Owner == _entityRef)
                Flash(recoverFlashColor);
        }

        private void OnAccessoryRestored(EventAccessoryRestored e)
        {
            if (e.Owner == _entityRef)
                Flash(recoverFlashColor);
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

        // Heal/shield/rift-mark-application/spawn funnel through here - extends the priority lock
        // FlashPickup below respects, then applies, UNLESS a Damage flash is still active
        // (_damageFlashLockUntil), in which case this is silently skipped - Damage always wins and
        // can't be interrupted back out by anything lower. Among themselves these are still equal
        // priority (last caller overwrites), same as before Damage became a distinct top tier.
        private void Flash(Color color, float flashDuration)
        {
            if (Time.time < _damageFlashLockUntil)
                return;

            _priorityFlashLockUntil = Time.time + flashDuration;
            ApplyFlash(color, flashDuration);
        }

        // Highest-priority flash - OnEntityDamaged's own path. Always applies immediately
        // regardless of anything currently playing (heal/shield/rift-mark-application/spawn/
        // pickup), and locks out every one of those for its own duration so a hit landing right
        // after e.g. a heal always reads clearly instead of the heal visually winning.
        private void FlashDamage(Color color)
        {
            _damageFlashLockUntil = Time.time + duration;
            _priorityFlashLockUntil = Time.time + duration;
            ApplyFlash(color, duration);
        }

        // Lower-priority flash for a currency pickup landing on this character (see
        // FlyingCurrencyManager, called once its flying sprite actually arrives here - not on the
        // collect event itself). Silently skipped if a higher-priority Flash is still within its
        // own duration, so a pickup glow can never visually stomp a hit/heal/shield/rift-mark
        // reaction happening at the same moment. Does NOT itself extend the lock - a pickup glow
        // should never block a hit flash that lands right after it.
        public void FlashPickup(CurrencyType type)
        {
            if (Time.time < _priorityFlashLockUntil)
                return;

            ApplyFlash(ResolvePickupFlashColor(type), pickupFlashDuration);
        }

        private Color ResolvePickupFlashColor(CurrencyType type)
        {
            switch (type)
            {
                case CurrencyType.Experience: return expPickupFlashColor;
                case CurrencyType.Coin: return coinPickupFlashColor;
                case CurrencyType.RiftShard: return riftShardPickupFlashColor;
                default: return Color.white;
            }
        }

        private void ApplyFlash(Color color, float flashDuration)
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

        // Inverse of Die() above - clears the death tint and un-blocks QUpdate, since a respawning
        // player never goes through InitializeSprites again (that only runs once, from Awake).
        [Button]
        public void Respawn()
        {
            if (_dead == false || sprites == null)
                return;

            _dead = false;

            for (var i = 0; i < sprites.Length; i++)
                sprites[i].color = restColor;
        }
    }
}
