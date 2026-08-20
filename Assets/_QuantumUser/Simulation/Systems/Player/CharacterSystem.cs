namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class CharacterSystem : SystemSignalsOnly, ISignalOnEntityPrototypeMaterialized
    {
        // Seeds once the entire prototype is materialized, not from
        // ISignalOnComponentAdded<CharacterStats>: components are added one at a time and
        // CharacterStats lands before Health and Shield, so seeding from its add found neither and
        // skipped both - leaving MaxHealth at 0 (which reads as already-dead, so nothing could
        // damage it) and RechargeRate at 0 (so the shield never recharged).
        public void OnEntityPrototypeMaterialized(Frame f, EntityRef entity, EntityPrototypeRef prototypeRef)
        {
            // Fires for every materialized entity - projectiles, chunks, areas - so this is the
            // filter for "is this a character at all".
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            if (stats->CharacterData.IsValid == false)
            {
                Log.Error($"[Character] {entity} has CharacterStats with no CharacterData assigned - stats stay at zero");
                return;
            }

            CharacterData data = f.FindAsset(stats->CharacterData);

            // Stats first - the health and shield maxima are scaled by multipliers that live on them.
            SeedStats(data, stats);
            SeedHealth(f, entity, data, stats);
            SeedArmor(f, entity, data);
            SeedShield(f, entity, data, stats);
            SeedSkills(f, entity, data);
            SeedWeapon(f, entity, data);

            // Last, so a passive that scales max health lands on an already-seeded Health.
            ApplyPassive(f, entity, data, stats);

            Log.Debug($"[Character] seeded {entity} from {stats->CharacterData}");
        }

        // Every stat starts at its authored value; passives and upgrades then edit these in place,
        // so a hero who took nothing reads exactly as authored.
        private static void SeedStats(CharacterData data, CharacterStats* stats)
        {
            stats->DamageMultiplier = data.DamageMultiplier;
            stats->WeaponDamageMultiplier = data.WeaponDamageMultiplier;
            stats->SkillDamageMultiplier = data.SkillDamageMultiplier;

            stats->MoveSpeedMultiplier = data.MoveSpeedMultiplier;

            stats->MaxHealthMultiplier = data.MaxHealthMultiplier;
            stats->MaxShieldMultiplier = data.MaxShieldMultiplier;

            stats->CriticalChance = data.CriticalChance;
            stats->CriticalDamageMultiplier = data.CriticalDamageMultiplier;

            stats->ElementalChance = data.ElementalChance;

            stats->AttackSpeedMultiplier = data.AttackSpeedMultiplier;
            stats->ReloadSpeedMultiplier = data.ReloadSpeedMultiplier;

            stats->ProjectileSpeedMultiplier = data.ProjectileSpeedMultiplier;
            stats->AreaRadiusMultiplier = data.AreaRadiusMultiplier;

            stats->NearDamageMultiplier = data.NearDamageMultiplier;
            stats->FarDamageMultiplier = data.FarDamageMultiplier;

            stats->DashCooldownMultiplier = data.DashCooldownMultiplier;
            stats->SkillCooldownMultiplier = data.SkillCooldownMultiplier;
            stats->SkillDurationMultiplier = data.SkillDurationMultiplier;
            stats->KnockbackMultiplier = data.KnockbackMultiplier;

            stats->LifeSteal = data.LifeSteal;
            stats->OutgoingStatusDurationMultiplier = data.OutgoingStatusDurationMultiplier;

            stats->DamageReduction = data.DamageReduction;
            stats->KnockbackTakenMultiplier = data.KnockbackTakenMultiplier;
            stats->HealingReceivedMultiplier = data.HealingReceivedMultiplier;

            stats->PickupRangeMultiplier = data.PickupRangeMultiplier;
            stats->Luck = data.Luck;
            stats->ExperienceGainMultiplier = data.ExperienceGainMultiplier;
            stats->RiftShardGainMultiplier = data.RiftShardGainMultiplier;
            stats->CoinGainMultiplier = data.CoinGainMultiplier;
        }

        // Health may legitimately be absent (a stats-only entity), so this is not an error case.
        private static void SeedHealth(Frame f, EntityRef entity, CharacterData data, CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            health->MaxHealth = data.BaseMaxHealth * stats->MaxHealthMultiplier;
            health->CurrentHealth = health->MaxHealth * f.RuntimeConfig.DebugInitialHealthMultiplier;
            health->RegenRate = data.BaseHealthRegenRate;

            Log.Debug($"[Character] {entity} health seeded -> {health->CurrentHealth}/{health->MaxHealth} " +
                      $"(base {data.BaseMaxHealth} x mult {stats->MaxHealthMultiplier}, debug current x{f.RuntimeConfig.DebugInitialHealthMultiplier})");
        }

        private static void SeedArmor(Frame f, EntityRef entity, CharacterData data)
        {
            if (f.Unsafe.TryGetPointer<Armor>(entity, out var armor) == false)
                return;

            armor->Amount = data.BaseArmor;
        }

        // Shield is optional - an unshielded hero simply has no Shield component to seed. Only Max
        // scales; recharge delay and rate are their own stats, not a function of size.
        private static void SeedShield(Frame f, EntityRef entity, CharacterData data, CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == false)
                return;

            shield->Max = data.BaseMaxShield * stats->MaxShieldMultiplier + stats->BonusMaxShield;
            shield->Current = shield->Max * f.RuntimeConfig.DebugInitialShieldMultiplier;
            shield->RechargeDelay = data.ShieldRechargeDelay;
            shield->RechargeRate = data.ShieldRechargeRate;
            shield->RechargeTimer = FP._0;

            Log.Debug($"[Character] {entity} shield seeded -> {shield->Current}/{shield->Max} " +
                      $"at {shield->RechargeRate}/s after {shield->RechargeDelay}s");

            if (shield->Max > FP._0 && shield->RechargeRate <= FP._0)
                Log.Error($"[Character] {entity} has a shield but {data.name} authors ShieldRechargeRate 0 - it will never recharge");
        }

        // Both of these exist because Health.MaxHealth / Shield.Max are stored, not derived on read
        // - so a perk that changes a multiplier mid-run has to say so, or the change silently does
        // nothing. Call after any mid-run write to MaxHealthMultiplier / MaxShieldMultiplier.
        //
        // Current is rescaled to hold its ratio rather than kept as-is: a perk halving max health
        // shouldn't also be a free heal (nor should doubling it be a free chunk of missing health).
        public static void RefreshMaxHealth(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            if (f.Unsafe.TryGetPointer<Health>(entity, out var health) == false)
                return;

            FP newMax = f.FindAsset(stats->CharacterData).BaseMaxHealth * stats->MaxHealthMultiplier;

            if (newMax <= FP._0 || health->MaxHealth <= FP._0)
                return;

            FP ratio = health->CurrentHealth / health->MaxHealth;

            health->MaxHealth = newMax;
            health->CurrentHealth = FPMath.Clamp(newMax * ratio, FP._0, newMax);

            Log.Debug($"[Character] {entity} max health -> {newMax} (kept {ratio} full)");
        }

        public static void RefreshMaxShield(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            if (f.Unsafe.TryGetPointer<Shield>(entity, out var shield) == false)
                return;

            FP newMax = f.FindAsset(stats->CharacterData).BaseMaxShield * stats->MaxShieldMultiplier + stats->BonusMaxShield;

            if (newMax <= FP._0 || shield->Max <= FP._0)
                return;

            FP ratio = shield->Current / shield->Max;

            shield->Max = newMax;
            shield->Current = FPMath.Clamp(newMax * ratio, FP._0, newMax);

            Log.Debug($"[Character] {entity} max shield -> {newMax} (kept {ratio} full)");
        }

        // CharacterData is the single authoring source for which skills a hero uses - same
        // reasoning as SeedStats - so this overwrites whatever CharacterSkills.DashSkill/HeroSkill the
        // entity prototype happened to have baked, rather than only filling gaps. Skips a slot
        // left unassigned on the data asset (AssetRef default) instead of stomping it with an
        // empty AssetRef, so it's harmless to call before every hero's DashSkill/HeroSkill
        // is authored.
        private static void SeedSkills(Frame f, EntityRef entity, CharacterData data)
        {
            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return;

            if (data.DashSkill.IsValid == true)
                skills->DashSkill.Skill = data.DashSkill;

            if (data.HeroSkill.IsValid == true)
                skills->HeroSkill.Skill = data.HeroSkill;

            Log.Debug($"[Character] {entity} skills seeded -> DashSkill={skills->DashSkill.Skill}, HeroSkill={skills->HeroSkill.Skill}");
        }

        // CharacterData is the single authoring source for a hero's starting weapon too - same
        // reasoning as SeedSkills - so this overwrites whatever Weapon.WeaponData the entity
        // prototype happened to have baked (see WeaponSystem.OnAdded, which now skips Equip
        // entirely when that field is left empty on the prototype) via the normal Equip path,
        // which seeds perk-mutable stats/ammo/cooldowns correctly instead of just stomping the
        // AssetRef field directly.
        private static void SeedWeapon(Frame f, EntityRef entity, CharacterData data)
        {
            if (data.StartingWeapon.IsValid == false)
                return;

            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == false)
                return;

            WeaponSystem.Equip(f, entity, weapon, data.StartingWeapon);
            Log.Debug($"[Character] {entity} starting weapon seeded -> {data.StartingWeapon}");
        }

        private static void ApplyPassive(Frame f, EntityRef entity, CharacterData data, CharacterStats* stats)
        {
            if (data.Passive.IsValid == false)
                return;

            f.FindAsset(data.Passive).Apply(f, entity, stats);
        }
    }
}
