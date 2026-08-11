namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Max's Vendetta passive - reacts to Combat.qtn's OnHealthDamageApplied/OnShieldDamageApplied
    // (mark creation/refresh, damage accumulation - purely reactive, an enemy only becomes marked by
    // actually damaging Max first) and OnEntityKilled (mark consumption, heal, and - if Burning
    // Vengeance rank 3 is equipped - a radial fiery burst). The Overdrive-extension-on-Vendetta-kill
    // concept lives in MaxOverdriveReactionSystem now (Uncontrolled Fury rank 3's own separate
    // uncapped bonus), not here. Unfiltered - no Filter query, entities resolved directly off each
    // signal's own payload, same shape WeaponPerkReactionSystem already uses. Scoped to
    // Vendetta-passive holders purely by RevengeConfig's presence - never an "is this entity Max"
    // check. The mark itself lives on the enemy (RevengeMark, see Vendetta.qtn), not on the holder,
    // so any number of enemies can be marked by the same holder at once - no cap, no replacement
    // between different enemies. See docs/max-ascensions.md.
    //
    // A proactive "Max's own weapon hits also mark" hook was tried and reverted - with Vendetta's
    // +DamageBonus applying to any marked enemy, it meant every enemy near Max got marked (and
    // therefore got hit for bonus damage) the instant his auto-fire reached it, indistinguishable
    // from a baseline damage buff with zero player choice involved. Marking stays purely "revenge for
    // being hit" - Vendetta Strike (the Dash Ascension) is the one deliberate way to mark proactively.
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
                // Two floors beneath the damage-based heal, so a kill on a mark that barely damaged
                // Max (or a Vendetta Strike proactive mark with no banked StoredDamage at all) still
                // heals something meaningful - MinHealFraction off Max's own MaxHealth, and
                // EnemyMaxHealthFraction off the killed enemy's, so a genuinely tough kill heals more
                // than a Filler one even at the floor. Highest of all three wins.
                FP targetMaxHealth = f.Unsafe.TryGetPointer<Health>(target, out var targetHealth) == true ? targetHealth->MaxHealth : FP._0;

                FP heal = FPMath.Max(mark->StoredDamage * config->HealMultiplier,
                    FPMath.Max(health->MaxHealth * config->MinHealFraction, targetMaxHealth * config->EnemyMaxHealthFraction));

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

            // Burning Vengeance - spreads Burn to nearby enemies, scoped to a Vendetta-consuming
            // kill specifically (see FireMastery.qtn's own comment on why this shares
            // StatusSpreadOnDeath with Wildfire's any-Burning-death trigger instead of a dedicated
            // component).
            if (f.Unsafe.TryGetPointer<StatusSpreadOnDeath>(owner, out var spread) == true
                && spread->TriggerOnVendettaKill == true
                && f.Unsafe.TryGetPointer<Transform3D>(target, out var deathTransform) == true)
            {
                bool wasBurning = StatusEffectUtility.IsBurning(f, target);

                FireMasterySpreadUtility.SpreadBurn(f, deathTransform->Position, owner, target, spread->Radius, spread->BurnDuration, spread->BurnIntensity, spread->MaxTargets);

                // Burning Vengeance rank 3 - a genuine fiery burst (damage + Burn to everyone in
                // radius), on top of the ordinary spread above, only when the kill was already
                // Burning - not another spread-on-death chain.
                if (spread->HasFieryBurst == true && wasBurning == true)
                {
                    MaxAscensionUtility.ApplyRadialBurn(f, deathTransform->Position, spread->Radius, owner, FP._0, spread->BurnDuration, spread->BurnIntensity);
                }
            }
        }
    }
}
