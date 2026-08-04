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
    // pair for as long as StatusEffectUtility reports that status active, repositioned/rescaled to
    // the entity's live collider every frame (see EnemyMovementUtility.ResolveEntityCenter/
    // ResolveEntityRadius), offset per status type by that status's own [x]Offset field so effects
    // that would otherwise stack exactly on top of each other (e.g. stun above the head, burn at
    // the feet) can be spread out. An instance is released the instant it's no longer seen active in a
    // frame's filter pass - that covers both a status naturally expiring/being healed AND the
    // entity itself disappearing from the filter entirely (death, disconnect), since both look
    // identical here: "not seen this frame". Works for any entity with StatusEffects, not just
    // enemies - e.g. ShieldRegen/Haste are ally buffs.
    public class StatusEffectsManager : QuantumGlobalMonoBehaviour
    {
        public static StatusEffectsManager Instance;

        [SerializeField, Tooltip("StatusEffects.BurnRemaining - see StatusEffectUtility.IsBurning.")]
        private ParticleSystem burnParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 burnOffset;

        [SerializeField, Tooltip("StatusEffects.RiftMarkStacks - see StatusEffectUtility.IsRiftMarked. Rift Mark's own visible tell - it does nothing by itself, but this shows a target has been primed for whichever elemental reaction lands next.")]
        private ParticleSystem riftMarkParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 riftMarkOffset;

        [SerializeField, Tooltip("StatusEffects.IceRemaining (slow) - see StatusEffectUtility.IsSlowed.")]
        private ParticleSystem slowParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 slowOffset;

        [SerializeField, Tooltip("StatusEffects.AnticipationSlowRemaining - see StatusEffectUtility.IsAnticipationSlowed. Ice+RiftMark's Deep Freeze reaction - stretches attack windups, not a lockout, so it's separate from Stun. See docs/elemental-reactions.md.")]
        private ParticleSystem freezeParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 freezeOffset;

        [SerializeField, Tooltip("StatusEffects.StunRemaining - see StatusEffectUtility.IsStunned.")]
        private ParticleSystem stunParticlePrefab;
        [SerializeField, Tooltip("Local offset from the entity center, in reference-diameter-1 units (scaled by the entity's own scale, same convention as the prefab itself).")]
        private Vector3 stunOffset;

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

        private readonly StatusSlotTracker _burn = new();
        private readonly StatusSlotTracker _riftMark = new();
        private readonly StatusSlotTracker _slow = new();
        private readonly StatusSlotTracker _freeze = new();
        private readonly StatusSlotTracker _stun = new();
        private readonly StatusSlotTracker _root = new();
        private readonly StatusSlotTracker _rupture = new();
        private readonly StatusSlotTracker _haste = new();
        private readonly StatusSlotTracker _shieldRegen = new();
        private readonly StatusSlotTracker _explodeMark = new();

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

                _burn.Update(burnParticlePrefab, entity, StatusEffectUtility.IsBurning(frame, entity), center, scale, burnOffset);
                _riftMark.Update(riftMarkParticlePrefab, entity, StatusEffectUtility.IsRiftMarked(frame, entity), center, scale, riftMarkOffset);
                _slow.Update(slowParticlePrefab, entity, StatusEffectUtility.IsSlowed(frame, entity), center, scale, slowOffset);
                _freeze.Update(freezeParticlePrefab, entity, StatusEffectUtility.IsAnticipationSlowed(frame, entity), center, scale, freezeOffset);
                _stun.Update(stunParticlePrefab, entity, StatusEffectUtility.IsStunned(frame, entity), center, scale, stunOffset);
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
            _riftMark.EndFrame(riftMarkParticlePrefab);
            _slow.EndFrame(slowParticlePrefab);
            _freeze.EndFrame(freezeParticlePrefab);
            _stun.EndFrame(stunParticlePrefab);
            _root.EndFrame(rootParticlePrefab);
            _rupture.EndFrame(ruptureParticlePrefab);
            _haste.EndFrame(hasteParticlePrefab);
            _shieldRegen.EndFrame(shieldRegenParticlePrefab);
            _explodeMark.EndFrame(explodeMarkParticlePrefab);
        }

        // One tracker per status type - owns the held/pooled instance for every entity currently
        // showing that status. Kept as a plain nested class (not a shared static helper) so each
        // status's instances/bookkeeping stay independent even though all 9 share this exact shape.
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
    }
}
