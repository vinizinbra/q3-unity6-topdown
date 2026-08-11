namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Brute's Guardian ascension rank 3 - reacts to Combat.qtn's OnHealthDamageApplied/
    // OnShieldDamageApplied (same signals MaxVendettaSystem already reacts to) for the "ally in the
    // aura loses Shield/Health from an enemy hit" reactive DR proc. Unfiltered - no Filter query,
    // entities resolved directly off each signal's own payload, same shape MaxVendettaSystem/
    // WeaponPerkReactionSystem already use. Scans every ProtectorAura holder (co-op match sizes are
    // tiny, 0-4 Brutes) rather than tracking a reverse "which aura covers this player" lookup, since
    // that lookup would need updating every tick anyway as players move.
    [Preserve]
    public unsafe class BruteProtectorReactionSystem : SystemMainThread,
        ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied
    {
        private static readonly FP ReactiveDuration = FP.FromString("1.5");
        private static readonly FP ReactiveAmount = FP.FromString("0.15");
        private static readonly FP ReactiveCooldown = FP._4;

        public override void Update(Frame f)
        {
        }

        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            TryTriggerReactiveDamageReduction(f, target, owner);
        }

        public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            TryTriggerReactiveDamageReduction(f, target, owner);
        }

        // target = the ally that just took the hit; owner = the attacker (must be a live Enemy - a
        // friendly-fire/self-inflicted source shouldn't proc this).
        private static void TryTriggerReactiveDamageReduction(Frame f, EntityRef target, EntityRef owner)
        {
            if (f.Has<Enemy>(owner) == false || f.Has<CharacterStats>(target) == false)
                return;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false || status->GuardianReactiveCooldownRemaining > FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            if (TryFindCoveringGuardianAura(f, targetTransform->Position) == false)
                return;

            StatusEffectUtility.ApplyTemporaryDamageReduction(f, target, ReactiveDuration, ReactiveAmount);
            status->GuardianReactiveCooldownRemaining = ReactiveCooldown;
        }

        private static bool TryFindCoveringGuardianAura(Frame f, FPVector3 position)
        {
            var auras = f.Filter<Transform3D, ProtectorAura>();

            while (auras.Next(out EntityRef _, out Transform3D auraTransform, out ProtectorAura aura))
            {
                if (aura.HasReactiveDamageReduction == false)
                    continue;

                if ((auraTransform.Position - position).SqrMagnitude <= aura.Radius * aura.Radius)
                    return true;
            }

            return false;
        }
    }
}
