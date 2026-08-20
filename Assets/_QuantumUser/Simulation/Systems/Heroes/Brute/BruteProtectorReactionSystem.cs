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

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false
                || status->ReactiveDamageReductionCooldownRemaining > FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            // Every value comes off whichever Guardian aura is actually covering this ally - authored
            // on the Ascension asset, not constants here, so a balance pass never needs a code change.
            if (TryFindCoveringGuardianAura(f, targetTransform->Position, out FP amount, out FP duration, out FP cooldown) == false)
                return;

            StatusEffectUtility.ApplyTemporaryDamageReduction(f, target, duration, amount);
            status->ReactiveDamageReductionCooldownRemaining = cooldown;
        }

        // Picks the STRONGEST covering aura rather than the first found, so two Brutes running
        // different Guardian ranks give the ally the better proc instead of whichever happened to be
        // iterated first - the same "aura sources don't stack, the strongest wins" policy the
        // continuous aura DR itself follows (see StatusEffectUtility.ApplyAuraDamageReduction).
        private static bool TryFindCoveringGuardianAura(Frame f, FPVector3 position, out FP amount, out FP duration, out FP cooldown)
        {
            amount = FP._0;
            duration = FP._0;
            cooldown = FP._0;

            var auras = f.Filter<Transform3D, ProtectorAura>();

            while (auras.Next(out EntityRef _, out Transform3D auraTransform, out ProtectorAura aura))
            {
                if (aura.ReactiveDamageReductionAmount <= FP._0 || aura.ReactiveDamageReductionAmount <= amount)
                    continue;

                if ((auraTransform.Position - position).SqrMagnitude > aura.Radius * aura.Radius)
                    continue;

                amount = aura.ReactiveDamageReductionAmount;
                duration = aura.ReactiveDamageReductionDuration;
                cooldown = aura.ReactiveCooldownPerAlly;
            }

            return amount > FP._0;
        }
    }
}
