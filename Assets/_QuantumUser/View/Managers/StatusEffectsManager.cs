using QuantumUser.View.Util;

namespace QuantumUser.View.Managers
{
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;

    // Central owner of every status-effect particle in the match - a new status VFX only needs a
    // prefab reference added here, not a component placed on every affected prefab. Scans every
    // entity with a StatusEffects component each frame (frame.Filter<StatusEffects>()) and keeps a
    // held/pooled instance (EffectsManager.GetHeldInstance/ReleaseHeldInstance) per (entity, status)
    // pair for as long as StatusEffectUtility reports that status active. An instance is released
    // the instant it's no longer seen active in a frame's filter pass - that covers both a status
    // naturally expiring/being healed AND the entity itself disappearing from the filter entirely
    // (death, disconnect), since both look identical here: "not seen this frame". Works for any
    // entity with StatusEffects, not just enemies - e.g. ShieldRegen/Haste are ally buffs.
    //
    // Two positioning schemes coexist here:
    //  - Burn/Slow/Electrified use ParentedStatusSlotTracker: the instance becomes an actual child
    //    of the entity's own HitFeedback.BodyRoot (EnemyViewRig.EnemyRoot for an enemy, the hero's
    //    own transform for a player), and its Shape module points at HitFeedback.MainBodySprite (a
    //    rig's ReferenceSprite / a hero's hand-wired Torso sprite) so the particle conforms to that
    //    entity's actual silhouette and simply follows for free via Transform parenting - no manual
    //    per-frame repositioning needed. [x]Offset for these three is now a LOCAL offset from that
    //    body root's own pivot (which for an enemy is bottom-pivoted, NOT collider-center like
    //    before) - re-tune these three in-Editor after this change.
    //  - Every other status (Freeze/Stun/Stagger/Root/Rupture/Haste/ShieldRegen/ExplodeMark) still
    //    uses the original StatusSlotTracker: world-positioned every frame at the entity's live
    //    collider center + offset*scale (see EnemyMovementUtility.ResolveEntityCenter/
    //    ResolveEntityRadius), unparented. Not yet migrated - do the same conversion for these once
    //    the parented approach has been validated in-Editor.
    public class StatusEffectsManager : QuantumGlobalMonoBehaviour
    {
        public static StatusEffectsManager Instance;

        [SerializeField, Tooltip("StatusEffects.BurnRemaining - see StatusEffectUtility.IsBurning. Parented onto the entity's own HitFeedback.BodyRoot - see ParentedStatusSlotTracker.")]
        private ParticleSystem burnParticlePrefab;
        [SerializeField, Tooltip("Local offset from HitFeedback.BodyRoot's own pivot (bottom-pivoted for an enemy - NOT collider center like the unparented statuses below). Re-tune in-Editor.")]
        private Vector3 burnOffset;

        [SerializeField, Tooltip("StatusEffects.IceRemaining (slow) - see StatusEffectUtility.IsSlowed. Parented onto the entity's own HitFeedback.BodyRoot - see ParentedStatusSlotTracker.")]
        private ParticleSystem slowParticlePrefab;
        [SerializeField, Tooltip("Local offset from HitFeedback.BodyRoot's own pivot (bottom-pivoted for an enemy - NOT collider center like the unparented statuses below). Re-tune in-Editor.")]
        private Vector3 slowOffset;

        [SerializeField, Tooltip("StatusEffects.AnticipationSlowRemaining - see StatusEffectUtility.IsAnticipationSlowed. Applied directly by FreezeEffectData (a standalone skill effect) - stretches attack windups, not a lockout, so it's separate from Stun.")]
        private ParticleSystem freezeParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 freezeOffset;

        [SerializeField, Tooltip("StatusEffects.StunRemaining - see StatusEffectUtility.IsStunned.")]
        private ParticleSystem stunParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 stunOffset;

        [SerializeField, Tooltip("StatusEffects.ElectrifiedRemaining - see StatusEffectUtility.IsElectrified. Lightning's baseline (Shock) - periodically fires a Jolt (a brief Stagger) while active. See docs/elemental-reactions.md. Parented onto the entity's own HitFeedback.BodyRoot - see ParentedStatusSlotTracker.")]
        private ParticleSystem electrifiedParticlePrefab;
        [SerializeField, Tooltip("Local offset from HitFeedback.BodyRoot's own pivot (bottom-pivoted for an enemy - NOT collider center like the unparented statuses below). Re-tune in-Editor.")]
        private Vector3 electrifiedOffset;

        [SerializeField, Tooltip("StatusEffects.StaggerRemaining - see StatusEffectUtility.IsStaggered. Brief pause of the target's own action windup, never a full disable like Stun - naturally pulses once per Jolt tick (JoltStaggerDuration is short) and once per Shatter's own primary/area application, with no extra event needed for that per-application pulse.")]
        private ParticleSystem staggerParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 staggerOffset;

        [SerializeField, Tooltip("StatusEffects.RootRemaining - see StatusEffectUtility.IsRooted.")]
        private ParticleSystem rootParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 rootOffset;

        [SerializeField, Tooltip("StatusEffects.RuptureRemaining - see StatusEffectUtility.HasRuptureDebuff.")]
        private ParticleSystem ruptureParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 ruptureOffset;

        [SerializeField, Tooltip("StatusEffects.IntimidateRemaining - see StatusEffectUtility.IsIntimidated. Applied to enemies by Brute's Protector Aura.")]
        private ParticleSystem intimidateParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 intimidateOffset;

        [SerializeField, Tooltip("StatusEffects.HasteRemaining[] - see StatusEffectUtility.HasHasteBuff. Active while any of the 4 source slots is running.")]
        private ParticleSystem hasteParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 hasteOffset;

        [SerializeField, Tooltip("StatusEffects.ShieldRegenRemaining - see StatusEffectUtility.HasShieldRegenBuff.")]
        private ParticleSystem shieldRegenParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 shieldRegenOffset;

        [SerializeField, Tooltip("ExplodeOnDeath presence (see ExplodeOnDeath.qtn/DamageUtility.TryMarkExplodeOnDeath) - not a StatusEffects field, so it's driven by its own frame.Filter<ExplodeOnDeath>() pass below rather than the StatusEffects loop above. Shows on any enemy currently primed to blow up on death, regardless of which hero's upgrade (Max's Berserk or Pixie's bomb) marked it.")]
        private ParticleSystem explodeMarkParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 explodeMarkOffset;

        private readonly ParentedStatusSlotTracker _burn = new();
        private readonly ParentedStatusSlotTracker _slow = new();
        private readonly StatusSlotTracker _freeze = new();
        private readonly StatusSlotTracker _stun = new();
        private readonly ParentedStatusSlotTracker _electrified = new();
        private readonly StatusSlotTracker _stagger = new();
        private readonly StatusSlotTracker _root = new();
        private readonly StatusSlotTracker _rupture = new();
        private readonly StatusSlotTracker _haste = new();
        private readonly StatusSlotTracker _shieldRegen = new();
        private readonly StatusSlotTracker _explodeMark = new();

        // Resolved once per entity (not per status, not per frame after the first hit) - see
        // ResolveHost. Cached rather than a fresh GetComponent every frame since every one of
        // Burn/Slow/Electrified on the same entity needs the exact same HitFeedback.
        private readonly Dictionary<EntityRef, HitFeedback> _hostCache = new();
        private readonly HashSet<EntityRef> _hostsSeenThisFrame = new();
        private List<EntityRef> _staleHostBuffer;

        // Same caching pattern as ProjectileView.ResolveMuzzleTransform - FindFirstObjectByType is
        // the expensive part, and Unity's overloaded null-check on a destroyed Object makes this
        // self-healing across a scene reload/reconnect for free.
        private static QuantumEntityViewUpdater _entityViewUpdater;

        private void Awake()
        {
            Instance = this;
        }

        public override void QStart(QuantumGame game)
        {
        }

        public override void QLateUpdate(QuantumGame game)
        {
        }

        public override void QUpdate(QuantumGame game)
        {
            Frame frame = game.Frames.Predicted;
            if (frame == null || EffectsManager.Instance == null)
                return;

            var filtered = frame.Filter<StatusEffects>();
            while (filtered.Next(out EntityRef entity, out StatusEffects _))
            {
                Vector3 center = EnemyMovementUtility.ResolveEntityCenter(frame, entity).ToUnityVector3();
                // Prefabs are authored at a reference diameter of 1 (radius 0.5), so this scales by
                // the full diameter, not just the radius.
                float scale = EnemyMovementUtility.ResolveEntityRadius(frame, entity).AsFloat * 2f;
                HitFeedback host = ResolveHost(entity);

                _burn.Update(burnParticlePrefab, entity, StatusEffectUtility.IsBurning(frame, entity), host, burnOffset);
                _slow.Update(slowParticlePrefab, entity, StatusEffectUtility.IsSlowed(frame, entity), host, slowOffset);
                _freeze.Update(freezeParticlePrefab, entity, StatusEffectUtility.IsAnticipationSlowed(frame, entity), center, scale, freezeOffset);
                _stun.Update(stunParticlePrefab, entity, StatusEffectUtility.IsStunned(frame, entity), center, scale, stunOffset);
                _electrified.Update(electrifiedParticlePrefab, entity, StatusEffectUtility.IsElectrified(frame, entity), host, electrifiedOffset);
                _stagger.Update(staggerParticlePrefab, entity, StatusEffectUtility.IsStaggered(frame, entity), center, scale, staggerOffset);
                _root.Update(rootParticlePrefab, entity, StatusEffectUtility.IsRooted(frame, entity), center, scale, rootOffset);
                _rupture.Update(ruptureParticlePrefab, entity, StatusEffectUtility.HasRuptureDebuff(frame, entity), center, scale, ruptureOffset);
                _haste.Update(hasteParticlePrefab, entity, StatusEffectUtility.HasHasteBuff(frame, entity), center, scale, hasteOffset);
                _shieldRegen.Update(shieldRegenParticlePrefab, entity, StatusEffectUtility.HasShieldRegenBuff(frame, entity), center, scale, shieldRegenOffset);
            }

            // Separate pass - ExplodeOnDeath isn't a StatusEffects field, so it isn't caught by the
            // filter above.
            var explodeMarked = frame.Filter<ExplodeOnDeath>();
            while (explodeMarked.Next(out EntityRef entity, out ExplodeOnDeath _))
            {
                Vector3 center = EnemyMovementUtility.ResolveEntityCenter(frame, entity).ToUnityVector3();
                float scale = EnemyMovementUtility.ResolveEntityRadius(frame, entity).AsFloat * 2f;

                _explodeMark.Update(explodeMarkParticlePrefab, entity, true, center, scale, explodeMarkOffset);
            }

            _burn.EndFrame(burnParticlePrefab);
            _slow.EndFrame(slowParticlePrefab);
            _freeze.EndFrame(freezeParticlePrefab);
            _stun.EndFrame(stunParticlePrefab);
            _electrified.EndFrame(electrifiedParticlePrefab);
            _stagger.EndFrame(staggerParticlePrefab);
            _root.EndFrame(rootParticlePrefab);
            _rupture.EndFrame(ruptureParticlePrefab);
            _haste.EndFrame(hasteParticlePrefab);
            _shieldRegen.EndFrame(shieldRegenParticlePrefab);
            _explodeMark.EndFrame(explodeMarkParticlePrefab);

            PruneHostCache();
        }

        // Resolves (and caches) this entity's HitFeedback - the single source for both BodyRoot
        // (where to parent a status particle) and MainBodySprite (what the Shape module should
        // conform to). Returns null (and does not cache) if the entity's view doesn't exist yet -
        // e.g. StatusEffects lands the same tick the entity itself is created - so a Parented
        // tracker just retries next frame instead of ever caching a miss.
        private HitFeedback ResolveHost(EntityRef entity)
        {
            _hostsSeenThisFrame.Add(entity);

            if (_hostCache.TryGetValue(entity, out HitFeedback cached) && cached != null)
                return cached;

            if (_entityViewUpdater == null)
                _entityViewUpdater = FindFirstObjectByType<QuantumEntityViewUpdater>();

            if (_entityViewUpdater == null)
                return null;

            QuantumEntityView view = _entityViewUpdater.GetView(entity);
            if (view == null)
                return null;

            HitFeedback host = view.GetComponent<HitFeedback>();
            if (host != null)
                _hostCache[entity] = host;

            return host;
        }

        // Same "stale = not seen this frame" cleanup shape every StatusSlotTracker.EndFrame already
        // uses, just for the shared host cache instead of a per-status instance dictionary.
        private void PruneHostCache()
        {
            foreach (var pair in _hostCache)
            {
                if (_hostsSeenThisFrame.Contains(pair.Key))
                    continue;

                (_staleHostBuffer ??= new List<EntityRef>()).Add(pair.Key);
            }

            if (_staleHostBuffer != null)
            {
                foreach (var entity in _staleHostBuffer)
                    _hostCache.Remove(entity);

                _staleHostBuffer.Clear();
            }

            _hostsSeenThisFrame.Clear();
        }

        // One tracker per status type - owns the held/pooled instance for every entity currently
        // showing that status. Kept as a plain nested class (not a shared static helper) so each
        // status's instances/bookkeeping stay independent even though all 11 share this exact shape.
        private class StatusSlotTracker
        {
            private readonly Dictionary<EntityRef, ParticleSystem> _instances = new();
            private readonly HashSet<EntityRef> _seenThisFrame = new();
            private List<EntityRef> _staleBuffer;

            public void Update(ParticleSystem prefab, EntityRef entity, bool active, Vector3 center, float scale, Vector3 offset)
            {
                if (active == false)
                    return;

                _seenThisFrame.Add(entity);

                if (_instances.TryGetValue(entity, out ParticleSystem instance) == false)
                {
                    instance = EffectsManager.Instance.GetHeldInstance(prefab);
                    instance?.Play();
                    _instances[entity] = instance;
                }

                if (instance != null)
                {
                    instance.transform.SetPositionAndRotation(center + offset * scale, Quaternion.identity);
                    instance.transform.localScale = Vector3.one * scale;
                }
            }

            // Releases every instance whose entity wasn't touched this frame, then resets for the
            // next frame's pass.
            public void EndFrame(ParticleSystem prefab)
            {
                foreach (var pair in _instances)
                {
                    if (_seenThisFrame.Contains(pair.Key))
                        continue;

                    EffectsManager.Instance.ReleaseHeldInstance(prefab, pair.Value);
                    (_staleBuffer ??= new List<EntityRef>()).Add(pair.Key);
                }

                if (_staleBuffer != null)
                {
                    foreach (var entity in _staleBuffer)
                        _instances.Remove(entity);

                    _staleBuffer.Clear();
                }

                _seenThisFrame.Clear();
            }
        }

        // Burn/Slow/Electrified's tracker - same held/pooled-per-entity shape as StatusSlotTracker
        // above, but the instance is made an actual child of the entity's own HitFeedback.BodyRoot
        // (so it follows for free via Transform parenting, no per-frame reposition) and its Shape
        // module is pointed at HitFeedback.MainBodySprite (so it conforms to that entity's own
        // silhouette instead of a generic circle sized off the collider radius).
        private class ParentedStatusSlotTracker
        {
            private readonly Dictionary<EntityRef, ParticleSystem> _instances = new();
            private readonly HashSet<EntityRef> _seenThisFrame = new();
            private List<EntityRef> _staleBuffer;

            public void Update(ParticleSystem prefab, EntityRef entity, bool active, HitFeedback host, Vector3 offset)
            {
                if (active == false)
                    return;

                _seenThisFrame.Add(entity);

                if (_instances.TryGetValue(entity, out ParticleSystem instance) == false)
                {
                    // Host not resolvable yet (e.g. the status lands the same tick the entity's own
                    // view spawns) - don't cache anything and just retry next frame this status is
                    // still active, same as StatusSlotTracker retries when EffectsManager itself
                    // isn't ready (see QUpdate's early-out above).
                    if (host == null || host.BodyRoot == null)
                        return;

                    instance = EffectsManager.Instance.GetHeldInstance(prefab);
                    if (instance != null)
                    {
                        instance.transform.SetParent(host.BodyRoot, worldPositionStays: false);
                        instance.transform.SetLocalPositionAndRotation(offset, Quaternion.identity);
                        instance.transform.localScale = Vector3.one;

                        if (host.MainBodySprite != null)
                        {
                            ParticleSystem.ShapeModule shape = instance.shape;
                            shape.shapeType = ParticleSystemShapeType.SpriteRenderer;
                            shape.spriteRenderer = host.MainBodySprite;
                        }

                        instance.Play();
                    }

                    _instances[entity] = instance;
                }
            }

            // Same release shape as StatusSlotTracker.EndFrame, plus one extra step: reparent back
            // onto EffectsManager itself before releasing - a held instance is only deactivated (not
            // destroyed) by ReleaseHeldInstance, so leaving it a child of this entity's own view would
            // destroy it for good the moment that view is torn down/pooled, silently poisoning the
            // pool with a dangling reference. Same pattern EnemyAttackVisualsView.ClearAnticipationIcon
            // already uses for the exact same reason.
            public void EndFrame(ParticleSystem prefab)
            {
                foreach (var pair in _instances)
                {
                    if (_seenThisFrame.Contains(pair.Key))
                        continue;

                    if (pair.Value != null && EffectsManager.Instance != null)
                        pair.Value.transform.SetParent(EffectsManager.Instance.transform, worldPositionStays: false);

                    EffectsManager.Instance.ReleaseHeldInstance(prefab, pair.Value);
                    (_staleBuffer ??= new List<EntityRef>()).Add(pair.Key);
                }

                if (_staleBuffer != null)
                {
                    foreach (var entity in _staleBuffer)
                        _instances.Remove(entity);

                    _staleBuffer.Clear();
                }

                _seenThisFrame.Clear();
            }
        }
    }
}
