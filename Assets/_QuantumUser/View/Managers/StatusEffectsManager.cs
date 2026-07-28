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
    // ResolveEntityRadius). An instance is released the instant it's no longer seen active in a
    // frame's filter pass - that covers both a status naturally expiring/being healed AND the
    // entity itself disappearing from the filter entirely (death, disconnect), since both look
    // identical here: "not seen this frame". Works for any entity with StatusEffects, not just
    // enemies - e.g. ShieldRegen/Haste are ally buffs.
    public class StatusEffectsManager : QuantumGlobalMonoBehaviour
    {
        public static StatusEffectsManager Instance;

        [SerializeField, Tooltip("StatusEffects.BurnRemaining - see StatusEffectUtility.IsBurning.")]
        private ParticleSystem burnParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.PoisonRemaining[] - see StatusEffectUtility.IsPoisoned. Active while any of the 5 stacks is running, regardless of stack count.")]
        private ParticleSystem poisonParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.IceRemaining (slow) - see StatusEffectUtility.IsSlowed.")]
        private ParticleSystem slowParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.StunRemaining - see StatusEffectUtility.IsStunned.")]
        private ParticleSystem stunParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.RootRemaining - see StatusEffectUtility.IsRooted. EffectsManager already plays a one-shot burst on Root application (OnEntityRooted); this is an additional held effect for the rooted duration itself, leave unassigned to skip it.")]
        private ParticleSystem rootParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.MarkRemaining - see StatusEffectUtility.HasMarkDebuff.")]
        private ParticleSystem markParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.HasteRemaining[] - see StatusEffectUtility.HasHasteBuff. Active while any of the 4 source slots is running.")]
        private ParticleSystem hasteParticlePrefab;
        [SerializeField, Tooltip("StatusEffects.ShieldRegenRemaining - see StatusEffectUtility.HasShieldRegenBuff.")]
        private ParticleSystem shieldRegenParticlePrefab;

        private readonly StatusSlotTracker _burn = new();
        private readonly StatusSlotTracker _poison = new();
        private readonly StatusSlotTracker _slow = new();
        private readonly StatusSlotTracker _stun = new();
        private readonly StatusSlotTracker _root = new();
        private readonly StatusSlotTracker _mark = new();
        private readonly StatusSlotTracker _haste = new();
        private readonly StatusSlotTracker _shieldRegen = new();

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
                // Prefabs are authored at a reference diameter of 1 (radius 0.5), same convention as
                // EffectsManager.OnEntityRooted, so this scales by the full diameter.
                float scale = EnemyMovementUtility.ResolveEntityRadius(frame, entity).AsFloat * 2f;

                _burn.Update(burnParticlePrefab, entity, StatusEffectUtility.IsBurning(frame, entity), center, scale);
                _poison.Update(poisonParticlePrefab, entity, StatusEffectUtility.IsPoisoned(frame, entity), center, scale);
                _slow.Update(slowParticlePrefab, entity, StatusEffectUtility.IsSlowed(frame, entity), center, scale);
                _stun.Update(stunParticlePrefab, entity, StatusEffectUtility.IsStunned(frame, entity), center, scale);
                _root.Update(rootParticlePrefab, entity, StatusEffectUtility.IsRooted(frame, entity), center, scale);
                _mark.Update(markParticlePrefab, entity, StatusEffectUtility.HasMarkDebuff(frame, entity), center, scale);
                _haste.Update(hasteParticlePrefab, entity, StatusEffectUtility.HasHasteBuff(frame, entity), center, scale);
                _shieldRegen.Update(shieldRegenParticlePrefab, entity, StatusEffectUtility.HasShieldRegenBuff(frame, entity), center, scale);
            }

            _burn.EndFrame(burnParticlePrefab);
            _poison.EndFrame(poisonParticlePrefab);
            _slow.EndFrame(slowParticlePrefab);
            _stun.EndFrame(stunParticlePrefab);
            _root.EndFrame(rootParticlePrefab);
            _mark.EndFrame(markParticlePrefab);
            _haste.EndFrame(hasteParticlePrefab);
            _shieldRegen.EndFrame(shieldRegenParticlePrefab);
        }

        // One tracker per status type - owns the held/pooled instance for every entity currently
        // showing that status. Kept as a plain nested class (not a shared static helper) so each
        // status's instances/bookkeeping stay independent even though all 8 share this exact shape.
        private class StatusSlotTracker
        {
            private readonly Dictionary<EntityRef, ParticleSystem> _instances = new();
            private readonly HashSet<EntityRef> _seenThisFrame = new();
            private List<EntityRef> _staleBuffer;

            public void Update(ParticleSystem prefab, EntityRef entity, bool active, Vector3 center, float scale)
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
                    instance.transform.SetPositionAndRotation(center, Quaternion.identity);
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
