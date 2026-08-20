namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Interrupts (cancels outright, not just pauses - see docs/revive.md) a reviver's own
    // ReviveChannel the instant they take damage. Exact same signal-driven shape
    // BruteProtectorReactionSystem already uses against the same two Combat.qtn signals. `target`
    // here is whoever just took the hit, i.e. the REVIVER - ReviveChannel only ever exists on a
    // reviver's own entity, never on the person being revived (who is Invulnerable while
    // Downed/KO, so these signals never fire for them at all - meaning a self-revive channel can
    // never be interrupted this way, by construction). Cancelling here does NOT reset the target's
    // own banked ReviveProgress - PlayerLifeStateSystem decays it back toward 0 gradually instead
    // (ReviveConfig.ReviveProgressDecayRate), so a teammate resuming the hold later picks up
    // roughly where the interrupted attempt left off rather than starting over from zero.
    [Preserve]
    public unsafe class ReviveDamageInterruptSystem : SystemMainThread, ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied
    {
        public override void Update(Frame f)
        {
        }

        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            TryInterruptRevive(f, target);
        }

        public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            TryInterruptRevive(f, target);
        }

        private static void TryInterruptRevive(Frame f, EntityRef target)
        {
            if (f.Has<ReviveChannel>(target) == false)
                return;

            ReviveUtility.Cancel(f, target);
        }
    }
}
