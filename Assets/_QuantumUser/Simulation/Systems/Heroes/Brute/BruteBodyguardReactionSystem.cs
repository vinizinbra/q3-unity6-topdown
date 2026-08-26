namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Brute's Bodyguard ascension ranks 2-3 - the payout half of the line. Reacts to Combat.qtn's
    // OnFreeHitGuardConsumed, fired the instant a Free Hit Guard actually eats a hit, and pays Brute
    // for the save: flat Shield back to him (rank 2+) and a knockback shockwave around the ally he
    // just saved (rank 3).
    //
    // Unfiltered - no Filter query, entities resolved straight off the signal payload, same shape
    // BruteProtectorReactionSystem/MaxVendettaSystem/WeaponPerkReactionSystem already use.
    //
    // The generic Free Hit Guard primitive deliberately knows nothing about any of this: it reports
    // only that a guard triggered and who granted it (see StatusEffects.qtn). What that save is WORTH
    // belongs to the ability that placed it, which is what keeps the primitive reusable by any future
    // hero/perk/consumable that wants to hand out a free block on its own terms.
    [Preserve]
    public unsafe class BruteBodyguardReactionSystem : SystemMainThread, ISignalOnFreeHitGuardConsumed
    {
        public override void Update(Frame f)
        {
        }

        // target = the ally whose guard just saved them; source = whoever granted that guard (Brute,
        // if this line placed it); attacker = whoever threw the punch, unused here.
        public void OnFreeHitGuardConsumed(Frame f, EntityRef target, EntityRef source, EntityRef attacker)
        {
            // Not a Bodyguard-granted guard - some other source handed out this free hit, so there is
            // nobody to pay. Absence of the component IS the check; no rank re-resolution needed.
            if (f.Unsafe.TryGetPointer<BodyguardUpgrade>(source, out var upgrade) == false)
                return;

            // Rank 2+ - Brute earns Shield for the save. Capped at his own Max like every other grant;
            // it's the same pool Juggernaut charges, so a well-placed guard is genuinely an alternative
            // route to keeping his own Accessory on.
            if (upgrade->ShieldReward > FP._0 && f.Unsafe.TryGetPointer<Shield>(source, out var shield) == true)
            {
                ShieldUtility.ApplyFlatShield(f, source, source, shield, upgrade->ShieldReward);
            }

            // Rank 3 - shockwave centred on the SAVED ALLY, not on Brute. By the time a guard triggers
            // Brute is usually long gone (the guard outlives the dash by design), so centring it on him
            // would routinely detonate it nowhere near the fight it exists to break up.
            if (upgrade->ShockwaveRadius > FP._0
                && f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
            {
                BruteAscensionUtility.ApplyRadialKnockback(f, targetTransform->Position, upgrade->ShockwaveRadius,
                    source, upgrade->ShockwaveForce);
            }
        }
    }
}
