namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Max's Vendetta passive - reacts to Combat.qtn's OnHealthDamageApplied/OnShieldDamageApplied
    // (mark creation/refresh, damage accumulation) and OnEntityKilled (mark consumption, heal, and -
    // if the Vendetta Rush Hero Skill Upgrade is equipped - extending the current Overdrive
    // activation). Unfiltered - no Filter query, entities resolved directly off each signal's own
    // payload, same shape WeaponPerkReactionSystem already uses. Scoped to Vendetta-passive holders
    // purely by RevengeConfig's presence - never an "is this entity Max" check. The mark itself
    // lives on the enemy (RevengeMark, see Vendetta.qtn), not on the holder, so any number of
    // enemies can be marked by the same holder at once - no cap, no replacement between different
    // enemies. See docs/max-vendetta-fire-mastery.md.
    [Preserve]
    public unsafe class MaxVendettaSystem : SystemMainThread,
        ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied, ISignalOnEntityKilled
    {
        public override void Update(Frame f)
        {
        }

        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            TryAccumulate(f, target, owner, amount);
        }

        public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            // Unbroken Spirit - off by default, so plain Shield damage does nothing for Vendetta
            // unless this upgrade is picked (see ShieldDamageCountsForRevenge.qtn once authored).
            if (f.Unsafe.TryGetPointer<ShieldDamageCountsForRevenge>(target, out _) == false)
                return;

            TryAccumulate(f, target, owner, amount);
        }

        // target = the Vendetta-passive holder that just took damage; owner = the attacker, which is
        // where the resulting mark is stored (RevengeMark), not on target.
        private static void TryAccumulate(Frame f, EntityRef target, EntityRef owner, FP amount)
        {
            if (f.Unsafe.TryGetPointer<RevengeConfig>(target, out var config) == false)
                return; // no Vendetta passive - nothing to track

            if (IsValidVendettaAttacker(f, owner) == false)
                return; // no source entity, dead/invulnerable, or non-hostile - no mark created

            f.AddOrGet<RevengeMark>(owner, out var mark);

            if (mark->MarkedBy != target)
            {
                // A different Vendetta holder claims this enemy - its previously stored damage
                // belonged to the old holder and is discarded, not carried over.
                mark->MarkedBy = target;
                mark->StoredDamage = FP._0;
            }

            mark->RemainingDuration = config->MarkDuration;
            mark->StoredDamage += amount;
        }

        private static bool IsValidVendettaAttacker(Frame f, EntityRef owner)
        {
            return owner != EntityRef.None
                && f.Exists(owner) == true
                && f.Has<Enemy>(owner) == true
                && f.Has<Invulnerable>(owner) == false;
        }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<RevengeMark>(target, out var mark) == false || mark->MarkedBy != owner)
                return; // this kill didn't consume owner's own Vendetta mark on target

            if (f.Unsafe.TryGetPointer<RevengeConfig>(owner, out var config) == true
                && f.Unsafe.TryGetPointer<Health>(owner, out var health) == true)
            {
                FP heal = mark->StoredDamage * config->HealMultiplier;

                if (heal > FP._0)
                {
                    // ApplyFlatHeal fires the generic EntityHealed itself (already clamped to missing
                    // Health); this event carries the pre-clamp requested amount instead - it's a
                    // one-shot VFX trigger (see MaxVendettaHealFxView), not a value display, so the
                    // small discrepancy on an already-near-full Max doesn't matter.
                    HealUtility.ApplyFlatHeal(f, owner, owner, health, heal);
                    f.Events.VendettaRevengeHealed(owner, heal);
                }
            }

            f.Remove<RevengeMark>(target);

            // Vendetta Rush - consuming the mark also extends the current Overdrive activation, on
            // top of whatever else this kill triggers below. No-ops if Overdrive isn't active right
            // now (see OverdriveUtility.TryExtend).
            if (f.Unsafe.TryGetPointer<VendettaRushExtension>(owner, out var rush) == true)
            {
                OverdriveUtility.TryExtend(f, owner, rush->ExtensionSeconds);
            }

            // Burning Vengeance - spreads Burn to nearby enemies, scoped to a Vendetta-consuming
            // kill specifically (see FireMastery.qtn's own comment on why this shares
            // StatusSpreadOnDeath with Wildfire's any-Burning-death trigger instead of a dedicated
            // component).
            if (f.Unsafe.TryGetPointer<StatusSpreadOnDeath>(owner, out var spread) == true
                && spread->TriggerOnVendettaKill == true
                && f.Unsafe.TryGetPointer<Transform3D>(target, out var deathTransform) == true)
            {
                FireMasterySpreadUtility.SpreadBurn(f, deathTransform->Position, owner, target, spread->Radius, spread->BurnDuration, spread->BurnIntensity, spread->MaxTargets);
            }
        }
    }
}
