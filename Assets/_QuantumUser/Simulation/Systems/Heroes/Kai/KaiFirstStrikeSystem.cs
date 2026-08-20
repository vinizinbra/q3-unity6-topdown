namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // First Strike rank 3 - killing an enemy that still carries this owner's own First Strike mark
    // banks a one-shot bonus onto the NEXT First Strike (see FirstStrikeUpgrade.KillEmpowerBonus/
    // PendingEmpowerBonus, spent in DamageUtility.ResolveOutgoingDamage).
    //
    // Replaces FirstStrikeMarkTimeoutSystem, which existed only to let a mark expire so the SAME
    // enemy could be First-Struck again every few seconds - deliberately removed: that made the line
    // a sustained-damage rotation against one target instead of an assassination/target-switching
    // mechanic. Every enemy now triggers First Strike exactly once, and rank 3's payoff is chaining
    // between targets.
    //
    // Unfiltered - no Filter query, entities resolved directly off the signal's own payload, same
    // shape MaxVendettaSystem/WeaponPerkReactionSystem already use.
    [Preserve]
    public unsafe class KaiFirstStrikeSystem : SystemMainThread, ISignalOnEntityKilled
    {
        public override void Update(Frame f)
        {
        }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<FirstStrikeUpgrade>(owner, out var upgrade) == false || upgrade->KillEmpowerBonus <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<FirstStrikeMark>(target, out var mark) == false || mark->MarkedBy != owner)
                return;

            // SET, never accumulate - a second kill before the bonus is spent re-arms it rather than
            // doubling it, so a kill streak can't compound into an unbounded multiplier.
            upgrade->PendingEmpowerBonus = upgrade->KillEmpowerBonus;

            Log.Debug($"[Skill] {owner}'s First Strike kill on {target} empowered the next opening (+{upgrade->KillEmpowerBonus})");
        }
    }
}
