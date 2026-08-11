namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Reacts to signals relevant to Max's Overdrive Ascension lines - Uncontrolled Fury's per-N-kills
    // extension plus its separate uncapped Vendetta-kill bonus (OnEntityKilled), Last Stand rank 2's
    // Retaliation proc and Rage's own reset-on-damage (OnHealthDamageApplied/OnShieldDamageApplied).
    // MUST be registered BEFORE MaxVendettaSystem in SystemSetup.User.cs - Uncontrolled Fury rank 3's
    // Vendetta-kill bonus has to read RevengeMark.MarkedBy on this same OnEntityKilled dispatch
    // before MaxVendettaSystem's own handler removes that mark.
    [Preserve]
    public unsafe class MaxOverdriveReactionSystem : SystemMainThread, ISignalOnEntityKilled, ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied
    {
        public override void Update(Frame f) { }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<UncontrolledFuryExtension>(owner, out var fury) == false)
                return;

            // Every-N-kills gating (N = fury->KillsPerExtension), not every kill - see
            // UncontrolledFuryExtension.qtn.
            fury->KillCount++;

            if (fury->KillCount >= fury->KillsPerExtension)
            {
                fury->KillCount = 0;

                FP remaining = fury->MaxExtension - fury->AccumulatedExtension;

                if (remaining > FP._0)
                {
                    FP extension = FPMath.Min(fury->PerKillExtension, remaining);

                    if (OverdriveUtility.TryExtend(f, owner, extension) == true)
                    {
                        fury->AccumulatedExtension += extension;
                    }
                }
            }

            // Rank 3's own separate, uncapped bonus - killing a target that still carries owner's own
            // Vendetta mark grants a flat extension independent of AccumulatedExtension above. Must
            // read this BEFORE MaxVendettaSystem's own OnEntityKilled (registered after this system)
            // removes the mark.
            if (fury->VendettaKillExtension > FP._0
                && f.Unsafe.TryGetPointer<RevengeMark>(target, out var mark) == true && mark->MarkedBy == owner)
            {
                OverdriveUtility.TryExtend(f, owner, fury->VendettaKillExtension);
            }
        }

        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            RageOverdriveUtility.ResetStacks(f, target);
            TryTriggerRetaliation(f, target);
        }

        public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            RageOverdriveUtility.ResetStacks(f, target);
            TryTriggerRetaliation(f, target);
        }

        // Last Stand rank 2 - a brief Weapon Damage buff whenever Max takes damage during an active
        // Overdrive activation, on its own proc cooldown so a flurry of hits doesn't refresh it every
        // single time. Scoped to Overdrive actually being active via RageOverdrive's own presence,
        // same gate ResetStacks itself relies on - LastStandUpgrade is safe to leave granted between
        // activations (see LastStandSkillAction's own comment).
        private static void TryTriggerRetaliation(Frame f, EntityRef target)
        {
            if (f.Has<RageOverdrive>(target) == false)
                return;

            if (f.Unsafe.TryGetPointer<LastStandUpgrade>(target, out var lastStand) == false || lastStand->HasRetaliation == false)
                return;

            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false || status->RetaliationCooldownRemaining > FP._0)
                return;

            status->RetaliationCooldownRemaining = FP._2;
            StatusEffectUtility.ApplyTemporaryWeaponDamage(f, target, lastStand->RetaliationDuration, lastStand->RetaliationDamageBonus);

            Log.Debug($"[Skill] {target} Retaliation procced (+{lastStand->RetaliationDamageBonus} Weapon Damage for {lastStand->RetaliationDuration}s)");
        }
    }
}
