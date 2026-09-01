namespace Quantum
{
    using System.Text;
    using Photon.Deterministic;

    // Diagnostics for the Rift Mutation system. Log-only by design - there is no HUD for any of
    // this, so the console is the one place a designer can see WHY a mutation didn't show up or
    // what a build actually resolved to.
    //
    // Everything here is read-only and side-effect free, so it is always safe to call. The dumps go
    // out at Debug level (stripped in Release, see the project's own logging notes), so leaving the
    // Grant-time call in permanently costs a real build nothing.
    public static unsafe class RiftMutationDebugUtility
    {
        // One line per grant, naming the mutation and its scope, followed by the full resolved state
        // of whoever took it - so a report of "this mutation does nothing" can be checked against
        // what was actually written rather than what was meant to be.
        public static void LogGranted(Frame f, EntityRef entity, RiftMutationData mutation)
        {
            Log.Debug($"[RiftMutation] granted '{ResolveName(mutation)}' ({mutation.Rarity}, {mutation.Scope} scope) to {entity}");

            LogPlayerState(f, entity);

            if (mutation.Scope == MutationScope.Run)
            {
                LogRunState(f);
            }
        }

        // Why a candidate was not offered. The single most useful line in this file: an
        // incompatibility or a run-scope duplicate is otherwise completely invisible - the mutation
        // just quietly stops appearing.
        public static void LogFiltered(Frame f, AssetRef<RiftMutationData> mutationRef, string reason)
        {
            Log.Debug($"[RiftMutation] '{ResolveName(f, mutationRef)}' not offered - {reason}");
        }

        // Everything one player's mutations have actually resolved to: what they own, the Accessory
        // state those mutations move, the two counters that are otherwise unobservable, and the
        // final weapon stats AFTER the full resolution pipeline (see
        // WeaponSystem.ApplyOwnerWeaponModifiers).
        public static void LogPlayerState(Frame f, EntityRef entity)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append($"[RiftMutation] {entity} state\n");
            builder.Append($"  mutations: {DescribePicks(f, entity)}\n");
            builder.Append($"  accessory: {DescribeAccessory(f, entity)}\n");

            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == true)
            {
                builder.Append($"  critical focus: {stats->CritFocusProgress}/{stats->CritFocusThreshold} crits (-{stats->CritFocusCooldownReduction}s on trigger)\n");
                builder.Append($"  emergency dash: {DescribeEmergencyDash(f, entity, stats)}\n");
                builder.Append($"  skill: damage x{stats->SkillDamageMultiplier}, cooldown rate x{stats->SkillCooldownMultiplier}, area x{stats->AreaRadiusMultiplier}, center focus +{stats->SkillCenterFocusBonus}\n");
                builder.Append($"  money talks: {DescribeMoneyTalks(stats)}\n");
                builder.Append($"  danger pay: {DescribeDangerPay(f, entity, stats)}\n");
                builder.Append($"  pressure cooker: {DescribePressureCooker(stats)}\n");
                builder.Append($"  no safety net: {DescribeNoSafetyNet(f, entity, stats)}\n");
                builder.Append($"  scavenger rush: {DescribeScavenger(stats)}\n");
                builder.Append($"  overkill: {DescribeOverkill(stats)}\n");
                builder.Append($"  blood money: {DescribeBloodMoney(stats)}\n");
                builder.Append($"  second wind: {DescribeSecondWind(stats)}\n");
                builder.Append($"  dead weight: {DescribeDeadWeight(f, entity)}\n");
                builder.Append($"  weapon stats: {DescribeWeapon(f, entity, stats)}\n");
            }
            else
            {
                builder.Append("  (no CharacterStats)\n");
            }

            Log.Debug(builder.ToString());
        }

        // The shared, run-wide half - what every player in the match is currently subject to, plus
        // the multipliers those raw fields actually resolve to (a raw "+0.4 density bonus" is much
        // less useful than the 1.4x-times-the-current-phase-ramp the Director is really using).
        public static void LogRunState(Frame f)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("[RiftMutation] run-wide encounter modifiers\n");
            builder.Append($"  run mutations: {DescribeRunPicks(f)}\n");
            builder.Append($"  enemy max health bonus: {f.Global->EnemyMaxHealthBonus} (boss resolves to x{EncounterModifierUtility.ResolveEnemyHealthMultiplier(f, EnemyTier.Boss)}, others x{EncounterModifierUtility.ResolveEnemyHealthMultiplier(f, EnemyTier.Normal)})\n");
            builder.Append($"  enemy damage: x{EncounterModifierUtility.ResolveEnemyDamageMultiplier(f)}\n");
            builder.Append($"  spawn density: x{EncounterModifierUtility.ResolveSpawnDensityMultiplier(f)} (flat bonus {f.Global->EnemySpawnDensityBonus}, phase ramp x{EncounterModifierUtility.ResolvePhaseRamp(f)})\n");
            builder.Append($"  elite group weight: x{(f.Global->EliteGroupWeightMultiplier <= 0 ? 1 : f.Global->EliteGroupWeightMultiplier)}\n");
            builder.Append($"  rift shard gain: x{EncounterModifierUtility.ResolveRiftShardGainMultiplier(f)}\n");

            Log.Debug(builder.ToString());
        }

        public static string ResolveName(Frame f, AssetRef<RiftMutationData> mutationRef)
        {
            return ResolveName(f.FindAsset(mutationRef));
        }

        // DisplayName is authored per asset and is what the player actually sees on the card, so it
        // is the right thing to log; the asset name is the fallback for an unauthored one.
        private static string ResolveName(RiftMutationData mutation)
        {
            if (mutation == null)
                return "<unresolved>";

            return string.IsNullOrEmpty(mutation.DisplayName) == false ? mutation.DisplayName : mutation.name;
        }

        private static string DescribePicks(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<RiftMutationPicks>(entity, out var picks) == false)
                return "none";

            StringBuilder builder = new StringBuilder();
            var picked = picks->Picked;

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i].IsValid == false)
                    continue;

                if (builder.Length > 0)
                    builder.Append(", ");

                builder.Append(ResolveName(f, picked[i]));
            }

            return builder.Length > 0 ? builder.ToString() : "none";
        }

        private static string DescribeRunPicks(Frame f)
        {
            StringBuilder builder = new StringBuilder();
            var runPicks = f.Global->RunMutationPicks;

            for (int i = 0; i < runPicks.Length; i++)
            {
                if (runPicks[i].IsValid == false)
                    continue;

                if (builder.Length > 0)
                    builder.Append(", ");

                builder.Append(ResolveName(f, runPicks[i]));
            }

            return builder.Length > 0 ? builder.ToString() : "none";
        }

        private static string DescribeAccessory(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<AccessoryGuard>(entity, out var guard) == false)
                return "no AccessoryGuard (mechanic disabled, or RuntimeConfig.AccessoryGuardConfig unassigned)";

            if (guard->Disabled == true)
                return "DISABLED for this player (Last Bastion)";

            string reserve = f.Unsafe.TryGetPointer<AccessoryEmergencyReserve>(entity, out var emergency) == true
                ? $"emergency reserve {emergency->Charges} charge(s) left, restores to {emergency->RestoreDurability}" +
                  (emergency->Charges == 0 ? " - ALREADY CONSUMED" : "")
                : "no emergency reserve";

            return $"{guard->CurrentDurability}/{guard->MaxDurability}, state {guard->State}, {reserve}";
        }

        // Unlimited while affordable, so the only interesting number is the price and whether the
        // player can currently pay it - see SkillSystem.TryPayEmergencyActivation.
        private static string DescribeEmergencyDash(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (stats->EmergencyDashHealthCost <= 0)
                return "not granted";

            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return $"granted (costs {stats->EmergencyDashHealthCost * 100}% max health)";

            FP cost = health->MaxHealth * stats->EmergencyDashHealthCost;

            // Usable whenever there is anything above the 1-health floor to spend - a player who
            // can't cover the full price still gets the Dash and is left at 1.
            bool usable = health->CurrentHealth > FP._1;

            return $"costs {cost} health per Dash ({stats->EmergencyDashHealthCost * 100}% of max) - " +
                   (usable ? $"usable now at {health->CurrentHealth} health" : "UNUSABLE, already at the 1-health floor");
        }

        // Live, balance-dependent, and therefore impossible to read off the asset alone - the whole
        // reason this one is worth dumping.
        private static string DescribeMoneyTalks(CharacterStats* stats)
        {
            if (stats->CoinDamagePerHundred <= 0)
                return "not granted";

            FP bonus = CoinUtility.ResolveDamageBonus(stats);

            return $"+{bonus * 100}% damage right now from {stats->Coins} coins " +
                   $"(+{stats->CoinDamagePerHundred * 100}% per {CoinUtility.CoinsPerDamageStep}, cap +{stats->CoinDamageMaxBonus * 100}%)";
        }

        private static string DescribeDangerPay(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (stats->DangerPayHealthThreshold <= 0)
                return "not granted";

            bool active = MutationModifierUtility.IsInDanger(f, entity, stats);

            return $"{(active ? "ACTIVE" : "inactive")} (below {stats->DangerPayHealthThreshold * 100}% health -> " +
                   $"+{stats->DangerPayDamageBonus * 100}% damage, +{stats->DangerPayMoveSpeedBonus * 100}% move speed)";
        }

        private static string DescribePressureCooker(CharacterStats* stats)
        {
            if (stats->PressureCookerDamagePerSecond <= 0)
                return "not granted";

            return $"+{MutationModifierUtility.ResolvePressureCookerBonus(stats) * 100}% damage from {stats->SafeTimeSeconds}s safe " +
                   $"(+{stats->PressureCookerDamagePerSecond * 100}%/s, cap +{stats->PressureCookerMaxBonus * 100}%)";
        }

        private static string DescribeNoSafetyNet(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (stats->NoSafetyNetDamageBonus <= 0)
                return "not granted";

            return AccessoryGuardUtility.IsExposed(f, entity) == true
                ? $"ACTIVE (+{stats->NoSafetyNetDamageBonus * 100}% damage, accessory is off)"
                : $"inactive (accessory equipped; would give +{stats->NoSafetyNetDamageBonus * 100}%)";
        }

        private static string DescribeScavenger(CharacterStats* stats)
        {
            if (stats->ScavengerRequiredPickups == 0)
                return "not granted";

            return $"{stats->ScavengerPickupCount}/{stats->ScavengerRequiredPickups} pickups, " +
                   $"{stats->ScavengerWindowRemaining}s left in window (of {stats->ScavengerWindow}s)";
        }

        private static string DescribeOverkill(CharacterStats* stats)
        {
            if (stats->OverkillConversion <= 0)
                return "not granted";

            return $"{stats->OverkillConversion * 100}% of excess damage as a blast, radius {stats->OverkillRadius}";
        }

        private static string DescribeBloodMoney(CharacterStats* stats)
        {
            if (stats->CoinLossPercentOnHpDamage <= 0)
                return "not granted";

            return $"loses {stats->CoinLossPercentOnHpDamage * 100}% of {stats->Coins} coins per HP hit " +
                   $"(= {FPMath.Floor(stats->Coins * stats->CoinLossPercentOnHpDamage)}), coin gain x{stats->CoinGainMultiplier}";
        }

        private static string DescribeSecondWind(CharacterStats* stats)
        {
            return stats->SecondWindHealPercent <= 0
                ? "not granted"
                : $"heals {stats->SecondWindHealPercent * 100}% max health per accessory recovery";
        }

        // The one readout the spec explicitly asks to distinguish: the CALCULATED ceiling (what
        // upgrades accumulated) versus the EFFECTIVE one (what the cap allows). Seeing both is how
        // you confirm a suppressed +1 Charge is still owned rather than destroyed.
        private static string DescribeDeadWeight(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false || stats->DashChargeHardCap == 0)
                return "not granted";

            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return $"cap {stats->DashChargeHardCap} (no CharacterSkills)";

            byte effective = SkillSystem.ResolveEffectiveMaxStacks(f, entity, SkillSlotId.DashSkill, &skills->DashSkill);

            return $"dash charges calculated {skills->DashSkill.MaxStacks} -> effective {effective} (cap {stats->DashChargeHardCap}), " +
                   $"currently {skills->DashSkill.CurrentStacks}, cooldown rate x{stats->DashCooldownMultiplier}";
        }

        // The end of the weapon-stat pipeline, which is exactly where a "my magazine modifier
        // vanished on weapon pickup" bug would show itself. MagazineSize is the resolved, baked
        // value; the rest are the live multipliers applied per shot.
        private static string DescribeWeapon(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return "no Weapon equipped";

            return $"magazine {weapon->Ammo}/{weapon->MagazineSize} (bonus {stats->MagazineSizeBonus}, override {stats->MagazineSizeOverride}), " +
                   $"weapon damage x{stats->WeaponDamageMultiplier}, global damage x{stats->DamageMultiplier}, weapon instance x{weapon->DamageMultiplier}, " +
                   $"fire rate x{stats->AttackSpeedMultiplier}, reload rate x{stats->ReloadSpeedMultiplier}, " +
                   $"near x{stats->NearDamageMultiplier} / far x{stats->FarDamageMultiplier}, long-range pierce +{stats->LongRangePierceBonus}, " +
                   $"stagger {stats->WeaponStaggerChance * 100}% for {stats->WeaponStaggerDuration}s";
        }
    }
}
