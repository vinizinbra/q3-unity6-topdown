namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Reacts to signals relevant to Max's Overdrive Ascension lines:
    //  - OnEntityKilled: Uncontrolled Fury's per-N-kills Overdrive extension (including rank 3's
    //    larger Vendetta-marked-kill grant - which draws from the SAME capped pool, deliberately, so
    //    no kill loop can produce a permanent Overdrive), Ignition rank 2's Burning Ground drop, and
    //    Blood Debt rank 2's Rage refund on a Vendetta kill.
    //  - OnHealthDamageApplied/OnShieldDamageApplied: Rage's own loss-on-damage.
    //
    // MUST be registered BEFORE MaxVendettaSystem in SystemSetup.User.cs - both react to the same
    // OnEntityKilled dispatch, and the two Vendetta-kill reactions here have to read
    // RevengeMark.MarkedBy before MaxVendettaSystem's own handler removes that mark.
    [Preserve]
    public unsafe class MaxOverdriveReactionSystem : SystemMainThread, ISignalOnEntityKilled, ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied
    {
        public override void Update(Frame f) { }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            // Read once, before anything else here can consume it - MaxVendettaSystem removes the
            // mark on its own pass over this same signal, and both reactions below need to know.
            bool wasVendettaMarked = f.Unsafe.TryGetPointer<RevengeMark>(target, out var mark) == true && mark->MarkedBy == owner;

            TryExtendOverdrive(f, owner, wasVendettaMarked);
            TryDropBurningGround(f, target, owner);
            TryRefundRage(f, owner, wasVendettaMarked);
        }

        // Uncontrolled Fury - every KillsPerExtension kills (not every kill) grants PerKillExtension
        // seconds, or VendettaKillExtension instead when the kill consumed one of this owner's own
        // Vendetta marks (rank 3). BOTH draw from the single AccumulatedExtension/MaxExtension pool,
        // reset fresh every activation by UncontrolledFurySkillAction - the spec's hard requirement
        // that no uncapped kill loop can produce a permanent Overdrive. The Vendetta bonus replaces
        // the ordinary grant for that kill rather than stacking on top of it, so a marked kill is
        // worth more, not double-counted.
        private static void TryExtendOverdrive(Frame f, EntityRef owner, bool wasVendettaMarked)
        {
            // KillsPerExtension is 0 on the ledger BerserkSkillData seeds - Uncontrolled Fury itself
            // raises it, so this whole branch no-ops for a build that never picked the line.
            if (f.Unsafe.TryGetPointer<OverdriveExtension>(owner, out var ledger) == false || ledger->KillsPerExtension == 0)
                return;

            ledger->KillCount++;

            if (ledger->KillCount < ledger->KillsPerExtension)
                return;

            ledger->KillCount = 0;

            FP requested = wasVendettaMarked == true && ledger->VendettaKillExtension > FP._0
                ? ledger->VendettaKillExtension
                : ledger->PerKillExtension;

            // TryExtend does the clamping and the booking against MaxExtension itself - see
            // OverdriveUtility, the single place that ceiling is enforced for every source.
            OverdriveUtility.TryExtend(f, owner, requested);
        }

        // Ignition rank 2 - a Burning enemy killed while Max is at max Rage leaves a burning-ground
        // patch where it died. Reworked from the old distance-paced "drop a patch every N units
        // travelled" OnGoing spawn: this version only ever pays off when Ignition's own Burn is
        // actually doing work, which is what ties the line together instead of being a second,
        // independent trail mechanic.
        private static void TryDropBurningGround(Frame f, EntityRef target, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<IgnitionUpgrade>(owner, out var ignition) == false || ignition->HasBurningGround == false)
                return;

            if (RageOverdriveUtility.IsAtMaxRage(f, owner) == false)
                return;

            if (StatusEffectUtility.IsBurning(f, target) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            MaxAscensionUtility.SpawnBurningGround(f, owner, ignition, transform->Position);
        }

        // Blood Debt rank 2 - killing a Vendetta-marked enemy refunds Rage, so Max's revenge loop
        // feeds his own Overdrive uptime. A no-op outside an active Overdrive: TryAdvanceStack itself
        // requires a live RageOverdrive component.
        private static void TryRefundRage(Frame f, EntityRef owner, bool wasVendettaMarked)
        {
            if (wasVendettaMarked == false)
                return;

            if (f.Unsafe.TryGetPointer<RevengeConfig>(owner, out var config) == false || config->RageOnVendettaKill == 0)
                return;

            for (int i = 0; i < config->RageOnVendettaKill; i++)
            {
                RageOverdriveUtility.TryAdvanceStack(f, owner);
            }
        }

        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            RageOverdriveUtility.ResetStacks(f, target);
        }

        public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            RageOverdriveUtility.ResetStacks(f, target);
        }
    }
}
