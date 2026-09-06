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

        [SerializeField, Tooltip("Shared player FX config - when assigned, its flash colors/durations overwrite every field below once in Awake (see ApplyFxConfig), so all heroes read the same values instead of each prefab carrying its own drifting copy. Enemies/objects also using HitFeedback leave this empty and keep their own local values below.")]
        private PlayerFxConfig fxConfig;

        [SerializeField, Tooltip("Hand-wire this hero's Torso sprite (one of the sprites above) per prefab - this is the SpriteRenderer StatusEffectsManager points a parented status particle's Shape module at, so Burn/Slow/Electrified conform to this hero's own silhouette instead of a generic circle. Enemies leave this empty and get it set at runtime from EnemyViewRig.ReferenceSprite instead - see SetRig.")]
        private SpriteRenderer mainBodySprite;

        // See mainBodySprite's own tooltip. Enemies get this set in SetRig; heroes get it from the
        // Inspector field directly (already populated before Awake runs, since it's serialized).
        public SpriteRenderer MainBodySprite => mainBodySprite;

        // Parent transform StatusEffectsManager attaches a status particle to. Defaults to this
        // component's own transform in Awake (correct for a hero - CharView.viewTransform is that
        // same GameObject, there's no separate rig root) and is overwritten in SetRig for an enemy,
        // whose actual visible body sits one level down on EnemyViewRig.EnemyRoot instead.
        public Transform BodyRoot { get; private set; }

        // Drives the HideIf attributes below - once fxConfig is assigned, ApplyFxConfig always
        // overwrites the local fields, so showing them in the Inspector would be misleading dead
        // data.
        private bool HasFxConfig => fxConfig != null;

        // Sibling on the same GameObject for an enemy (see SetRig) - null for a player/Sentry, which
        // has no EnemyBlobAnimationView. Used only by OnJoltTriggered's punch-scale, since Electrified/
        // Jolt is currently an enemy-only status (enemies never deal elemental damage back - see
        // docs/elemental-reactions.md's "Current status").
        private EnemyBlobAnimationView _enemyBlobAnimationView;

        [Header("Hit Flash")]
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used for a Neutral-element hit (plain weapon/skill damage) - everything without a more specific color below. Ignored (hidden) once Fx Config above is assigned - see ApplyFxConfig.")]
        private Color flashColor = Color.white;
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used instead of flashColor when the hit's ElementType is Fire - i.e. a Burn tick (see StatusEffectSystem.TickBurn).")]
        private Color burnFlashColor = new Color(1f, 0.45f, 0.1f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Flash colour when the accessory is put back on - recovered off the ground, or repaired/replaced at the Merchant (EventAccessoryRecovered/EventAccessoryRestored). Low priority, so it never stomps a hit flash.")]
        private Color recoverFlashColor = Color.cyan;
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Flash colour when the Accessory Guard eats a hit entirely (EventAccessoryBlocked). BLUE, not a damage colour: nothing was actually lost, so it must never read as being hurt. Matches EffectsManager.accessoryBlockedEffectColor so the character flash and the impact spark speak the same language. See docs/accessory-guard.md.")]
        private Color blockFlashColor = new Color(0.25f, 0.6f, 1f);

        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Flash colour when a Free Hit Guard is spent (EventFreeHitGuardConsumed - Brute's Bodyguard today). CYAN: same cool family as the accessory block above (both are negations, neither is damage) but a distinct hue, so which one just saved you is readable at a glance. Matches EffectsManager.freeHitGuardEffectColor.")]
        private Color freeHitGuardFlashColor = new Color(0.4f, 0.95f, 1f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used instead of flashColor/burnFlashColor when EventEntityDamaged.FrontalReduced is true (a FrontalDamageReduction enemy hit within its facing arc) - takes priority over the element color either way.")]
        private Color frontalReducedFlashColor = Color.gray;
        [SerializeField, HideIf(nameof(HasFxConfig))] private Color restColor = Color.clear;
        [SerializeField, HideIf(nameof(HasFxConfig))] private float duration = 0.1f;

        [Header("Jolt")]
        [SerializeField, Tooltip("Punch-scale strength played on EventJoltTriggered, via EnemyBlobAnimationView.PunchScale (enemy-only - see _enemyBlobAnimationView's own comment). Small by design - a brief flinch, not a big hit reaction. No color flash accompanies it - the first-element rest tint already shows Electrified is active for the whole duration (see UpdateElementalRestTint), so a separate per-Jolt flash on top was redundant (and, before that tint's own color was tuned, visually indistinguishable from it to the point of reading as a stuck flash).")]
        private Vector3 joltPunchScaleStrength = new Vector3(0.08f, 0.08f, 0f);
        [SerializeField] private float joltPunchScaleDuration = 0.15f;
        [SerializeField] private float joltPunchScaleFrequency = 20f;

        [Header("Heal Flash")]
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used on EventEntityHealed - e.g. FlyingShielder healing an ally enemy, or Zara's heal pulse.")]
        private Color healFlashColor = new Color(0.4f, 1f, 0.4f);

        [Header("Shield Flash")]
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used on EventEntityShielded - e.g. Bodyguard/Portable Cover granting Shield.")]
        private Color shieldFlashColor = new Color(0.4f, 0.75f, 1f);

        [Header("Death")]
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Applied the instant the entity dies, and held (hit flash stops overriding it) for the rest of the corpse's lingering duration.")]
        private Color deathColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        [Header("Pickup Flash")]
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used on FlashPickup for CurrencyType.Experience - see FlyingCurrencyManager.OnArrived.")]
        private Color expPickupFlashColor = new Color(0.3f, 0.55f, 1f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used on FlashPickup for CurrencyType.Coin.")]
        private Color coinPickupFlashColor = new Color(1f, 0.84f, 0.2f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used on FlashPickup for CurrencyType.RiftShard.")]
        private Color riftShardPickupFlashColor = new Color(1f, 0.35f, 0.75f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Used on FlashPickup for CurrencyType.Scrap - Lux's own pickup (see FlyingCurrencyManager). Warm scrap-metal orange, deliberately away from the blue/gold/pink the three currencies already occupy so a Lux hoovering Exp and Scrap at once can still tell them apart.")]
        private Color scrapPickupFlashColor = new Color(0.95f, 0.6f, 0.25f);
        [SerializeField, HideIf(nameof(HasFxConfig))] private float pickupFlashDuration = 0.35f;

        [Header("Elemental First-Hit Rest Tint")]
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("While StatusEffects.FirstElementApplied (the FIRST of Fire/Ice/Rock/Lightning to ever land a baseline status on this entity - see that field's own comment) is Fire AND Burn is still actually active, restColor is live-overridden to this (see UpdateElementalRestTint) - reverts back to this entity's own original rest color the instant Burn expires. Rock has no entry here (not requested) - it's simply left unhandled, restColor untouched. Ignored (hidden) once Fx Config above is assigned - see ApplyFxConfig.")]
        private Color fireRestTint = new Color(1f, 0.55f, 0.2f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Same as fireRestTint, for Element == Ice.")]
        private Color iceRestTint = new Color(0.4f, 0.9f, 1f);
        [SerializeField, HideIf(nameof(HasFxConfig)), Tooltip("Same as fireRestTint, for Element == Lightning.")]
        private Color lightningRestTint = new Color(1f, 0.9f, 0.3f);

        [Header("Freeze Mark")]
        [SerializeField, Tooltip("StatusEffects.AnticipationSlowRemaining - see StatusEffectUtility.IsAnticipationSlowed (applied directly by FreezeEffectData, a standalone skill effect).")]
        private Material freezeMaterial;

        private enum MarkState { Normal, Freeze }

        // Keyed by SpriteRenderer instance (not by this component) because the renderers live on
        // the pooled ViewPrefab (see ViewPrefabPool) while HitFeedback itself is a fresh instance
        // per enemy spawn - a plain instance field would just re-trust whatever material the
        // previous pool occupant happened to leave assigned. Baking each renderer's true original
        // once, the first time it's ever seen, means InitializeSprites can always force-restore to
        // it below instead of reading (possibly leaked) current state.
        private static readonly Dictionary<SpriteRenderer, Material> _bakedOriginalMaterials = new Dictionary<SpriteRenderer, Material>();

        private Material[] _originalMaterials;
        private MarkState _markState;
        private bool _dead;

        // restColor as authored/config-resolved, captured once in InitializeSprites BEFORE any
        // elemental tint can touch it - QUpdate reverts restColor back to this the moment
        // StatusEffects.FirstElementApplied's own status (Burn/Ice/Electrified/Intimidate) goes
        // inactive, so a recycled pooled enemy or a status that has genuinely worn off never leaves
        // the tint stuck on. See the "Elemental First-Hit Rest Tint" block below.
        private Color _originalRestColor;
        private bool _elementalTintActive;

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
            BodyRoot = transform;
            ApplyFxConfig();
            QuantumEvent.Subscribe<EventEntityDamaged>(this, OnEntityDamaged);
            QuantumEvent.Subscribe<EventJoltTriggered>(this, OnJoltTriggered);
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

        // Overwrites every locally-authored flash field with the shared config's values, once,
        // before anything can read them - keeps every other method in this file reading the same
        // plain private fields as before, unchanged. No-op (locals stand as authored) when
        // fxConfig is left unassigned, e.g. on enemy/object prefabs. Rift Mark's own flash fields
        // were removed from this class when Rift Mark itself was removed from the game (see
        // docs/elemental-reactions.md) - PlayerFxConfig may still carry its own now-unused
        // RiftMarkApplicationFlashColor/Duration fields, deliberately left alone here rather than
        // pruning someone else's ScriptableObject as a side effect of this change.
        private void ApplyFxConfig()
        {
            if (fxConfig == null)
                return;

            flashColor = fxConfig.FlashColor;
            burnFlashColor = fxConfig.BurnFlashColor;
            recoverFlashColor = fxConfig.RecoverFlashColor;
            blockFlashColor = fxConfig.BlockFlashColor;
            freeHitGuardFlashColor = fxConfig.FreeHitGuardFlashColor;
            frontalReducedFlashColor = fxConfig.FrontalReducedFlashColor;
            restColor = fxConfig.RestColor;
            duration = fxConfig.FlashDuration;
            fireRestTint = fxConfig.FireRestTint;
            iceRestTint = fxConfig.IceRestTint;
            lightningRestTint = fxConfig.LightningRestTint;
            healFlashColor = fxConfig.HealFlashColor;
            shieldFlashColor = fxConfig.ShieldFlashColor;
            deathColor = fxConfig.DeathColor;
            expPickupFlashColor = fxConfig.ExpPickupFlashColor;
            coinPickupFlashColor = fxConfig.CoinPickupFlashColor;
            riftShardPickupFlashColor = fxConfig.RiftShardPickupFlashColor;
            scrapPickupFlashColor = fxConfig.ScrapPickupFlashColor;
            pickupFlashDuration = fxConfig.PickupFlashDuration;
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
            mainBodySprite = rig.ReferenceSprite;
            BodyRoot = rig.EnemyRoot;
            _enemyBlobAnimationView = GetComponent<EnemyBlobAnimationView>();
            InitializeSprites();
        }

        private void InitializeSprites()
        {
            _tweens = new Tween[sprites.Length];
            _dead = false;
            _markState = MarkState.Normal;

            // Captured fresh on every spawn (not just once in Awake) - a pooled ViewPrefab reused for
            // a new enemy must start from whatever restColor is authored NOW, not a stale value left
            // over from the previous occupant's own elemental tint.
            _originalRestColor = restColor;
            _elementalTintActive = false;

            // Force-restores every sprite to its baked-original material on spawn - not just a
            // read, a write - so a pooled ViewPrefab that got recycled while still Frozen (see
            // Die() below) never shows the leftover material on the enemy now occupying it.
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

        // Freeze is a material swap, not a tween - the status is either active or it isn't, so
        // sprites just jump to the matching material and back rather than easing a color.
        protected override void QUpdate(QuantumGame game)
        {
            if (_dead || sprites == null)
                return;

            Frame frame = game.Frames.Predicted;
            if (frame == null)
                return;

            UpdateElementalRestTint(frame);

            MarkState state = freezeMaterial != null && StatusEffectUtility.IsAnticipationSlowed(frame, _entityRef)
                ? MarkState.Freeze
                : MarkState.Normal;

            if (state == _markState)
                return;

            _markState = state;
            Material material = state == MarkState.Freeze ? freezeMaterial : null;
            for (var i = 0; i < sprites.Length; i++)
                sprites[i].sharedMaterial = material != null ? material : _originalMaterials[i];
        }

        // Same live poll-and-toggle shape as the Freeze block above, for the SAME reason - a status
        // (Burn/Ice/Electrified/Intimidate) is either active or it isn't, and restColor should track
        // that live rather than getting permanently stuck once first set. ElementType.Rock resolves no
        // tint (ResolveElementRestTint returns null - not requested), so IsElementStatusActive is never
        // even reached for it below; this only ever activates for Fire/Ice/Lightning.
        private void UpdateElementalRestTint(Frame frame)
        {
            ElementType element = StatusEffectUtility.GetFirstElementApplied(frame, _entityRef);
            if (element == ElementType.Neutral)
                return;

            Color? tint = ResolveElementRestTint(element);
            if (tint.HasValue == false)
                return;

            bool active = IsElementStatusActive(frame, _entityRef, element);
            if (active == _elementalTintActive)
                return;

            _elementalTintActive = active;
            restColor = active ? tint.Value : _originalRestColor;

            // restColor is only ever READ by ApplyFlash as a future tween destination - reassigning
            // the field alone doesn't repaint anything already sitting on screen. Without actively
            // applying it here, an enemy whose last flash landed right before this transition (e.g. a
            // hit connecting the same tick Electrified expires) stays visually stuck at that old color
            // forever if nothing else happens to flash it again afterward. Stop-then-set, same as
            // Die()/Respawn() - cuts off (rather than fights) any flash tween still mid-animation.
            for (var i = 0; i < sprites.Length; i++)
            {
                _tweens[i].Stop();
                sprites[i].color = restColor;
            }
        }

        private static bool IsElementStatusActive(Frame frame, EntityRef entity, ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return StatusEffectUtility.IsBurning(frame, entity);
                case ElementType.Ice: return StatusEffectUtility.IsSlowed(frame, entity);
                case ElementType.Lightning: return StatusEffectUtility.IsElectrified(frame, entity);
                case ElementType.Rock: return StatusEffectUtility.IsIntimidated(frame, entity);
                default: return false;
            }
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

        // Fire/Ice/Lightning only - see UpdateElementalRestTint. Rock has no tint authored (not
        // requested), so it resolves null and IsElementStatusActive is never reached for it.
        private Color? ResolveElementRestTint(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return fireRestTint;
                case ElementType.Ice: return iceRestTint;
                case ElementType.Lightning: return lightningRestTint;
                default: return null;
            }
        }

        // No color flash - see joltPunchScaleStrength's own comment for why. The punch-scale is
        // enemy-only (_enemyBlobAnimationView is null for a player/Sentry) - see that field's own
        // comment.
        private void OnJoltTriggered(EventJoltTriggered e)
        {
            if (e.Target != _entityRef)
                return;

            _enemyBlobAnimationView?.PunchScale(joltPunchScaleStrength, joltPunchScaleDuration, joltPunchScaleFrequency);
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
                case CurrencyType.Scrap: return scrapPickupFlashColor;
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
        // without this a corpse that died while Frozen would linger showing that material under
        // the death tint, and (since QUpdate bails out once _dead is true, so
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
